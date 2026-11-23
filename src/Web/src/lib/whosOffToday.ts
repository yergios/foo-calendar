import { holidayAppliesToPerson } from "@/lib/absenceSegments";
import { addDays, formatDayMonth, formatTimeRange, nextWorkingDay } from "@/lib/dates";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto, type AbsenceType } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

// The "back on" date has to skip whatever else is already in the way: a bank holiday, and
// any absence that picks up where this one leaves off. So the absences and holidays this
// works from reach forward rather than covering today alone, wide enough to cover the
// return date of any realistic absence someone is in the middle of. Anything running past
// the horizon falls back to skipping weekends only.
export const HORIZON_DAYS = 120;

/** The last day the absences and holidays handed to whosOffToday need to cover. */
export function horizonFrom(todayIso: string): string {
  return addDays(todayIso, HORIZON_DAYS);
}

/** One person off today, and why. */
export interface OffTodayEntry {
  key: string;
  person: PersonDto;
  kind: "absence" | "holiday";
  /** The absence type's letter, or "H" for a bank holiday. */
  initial: string;
  typeLabel: string;
  /** Time range for a time-based absence, the return date for a whole-day one, or the
   * holiday's name. */
  detail: string;
  /** The first later working day they are back, for a whole-day absence only. */
  backOn: string | null;
}

// The list's sort order: time-based first (whoever's out for just part of the day is the
// one worth knowing about soonest), then everyone off for the whole day, holiday before
// personal reasons. Appointment is never actually whole-day (enforced server-side), so its
// entry here is unreachable in practice but keeps the map exhaustive.
const HOLIDAY_CATEGORY = 1;
const TIME_BASED_CATEGORY = 0;
const WHOLE_DAY_CATEGORY: Record<AbsenceType, number> = {
  Appointment: TIME_BASED_CATEGORY,
  Vacation: 2,
  Other: 3,
};

interface Ranked {
  category: number;
  entry: OffTodayEntry;
}

// Every date each person is already booked off again, from the whole-day absences in the
// window. A second absence starting the day after the first ends is perfectly legal - the
// server rejects overlapping absences, not adjacent ones - so without this the "back on"
// date would announce a return that the next absence immediately cancels. Time-based
// absences are left out on purpose: an appointment doesn't stop someone being back that
// day.
function wholeDayAbsenceDates(
  absences: readonly AbsenceDto[],
  horizonIso: string,
): Map<number, Set<string>> {
  const byPerson = new Map<number, Set<string>>();

  for (const absence of absences) {
    if (absence.startTime) continue;

    let dates = byPerson.get(absence.personId);
    if (dates === undefined) {
      dates = new Set();
      byPerson.set(absence.personId, dates);
    }
    // Capped at the horizon rather than the absence's own end: a year-long leave would
    // otherwise expand into hundreds of entries nobody reads past the first few of.
    for (let iso = absence.startDate; iso <= absence.endDate && iso <= horizonIso; ) {
      dates.add(iso);
      iso = addDays(iso, 1);
    }
  }

  return byPerson;
}

// What stops a given date counting as this person's first day back: their own region's
// bank holidays, and any whole-day absence already covering it. Someone with no home city
// set has no holiday calendar to check against, so only the absences apply. The linear scan
// is over one horizon's worth of holidays and runs a handful of times per person, so an
// index per person would cost more than it saves.
function unavailableCheckFor(
  person: PersonDto,
  bookedOff: Set<string> | undefined,
  holidays: readonly BankHolidayDto[],
): (iso: string) => boolean {
  const homeCity = person.homeCity;

  return (iso) =>
    bookedOff?.has(iso) === true ||
    (homeCity !== null &&
      holidays.some((h) => h.date === iso && holidayAppliesToPerson(h.region, homeCity)));
}

/**
 * Everyone off today, each person once: anyone with an absence covering today, and anyone
 * else with a bank holiday of their own today. Ordered time-based absences first, then
 * holidays, then whole-day absences by type, and by name within each.
 *
 * `people` is the whole team, regardless of any filter on the calendar: "who's off today"
 * is a global question. `absences` and `holidays` must cover today to `horizonFrom(today)`.
 */
export function whosOffToday(
  todayIso: string,
  people: readonly PersonDto[],
  absences: readonly AbsenceDto[],
  holidays: readonly BankHolidayDto[],
): OffTodayEntry[] {
  const peopleById = new Map(people.map((person) => [person.id, person]));
  const bookedOff = wholeDayAbsenceDates(absences, horizonFrom(todayIso));

  const absenceEntries = absences
    // The absences reach forward for the "back on" dates, so an absence starting next month
    // must not put anyone on today's list.
    .filter((absence) => absence.startDate <= todayIso && absence.endDate >= todayIso)
    .flatMap((absence): Ranked[] => {
      const person = peopleById.get(absence.personId);
      if (!person) return [];
      const backOn = absence.startTime
        ? null
        : nextWorkingDay(
            absence.endDate,
            unavailableCheckFor(person, bookedOff.get(person.id), holidays),
          );
      // The year is worth spelling out once the return date crosses into the next one:
      // "02/01" on its own reads as a date that has already been and gone.
      const detail =
        backOn === null
          ? formatTimeRange(absence.startTime, absence.endTime)
          : `Back on the ${formatDayMonth(backOn, backOn.slice(0, 4) !== todayIso.slice(0, 4))}`;
      return [
        {
          category: absence.startTime ? TIME_BASED_CATEGORY : WHOLE_DAY_CATEGORY[absence.type],
          entry: {
            key: `absence-${absence.id}`,
            person,
            kind: "absence",
            initial: ABSENCE_TYPE_INITIALS[absence.type],
            typeLabel: absence.type,
            detail,
            backOn,
          },
        },
      ];
    });

  // Anyone already listed for a personal absence today doesn't also need a "their region
  // happens to have a holiday" line - one reason they're off is enough.
  const alreadyListed = new Set(absenceEntries.map(({ entry }) => entry.person.id));

  const holidayEntries = people.flatMap((person): Ranked[] => {
    const homeCity = person.homeCity;
    if (alreadyListed.has(person.id) || !homeCity) return [];
    // The holidays reach past today for the "back on" dates, so a holiday three weeks out
    // must not put anyone on today's list.
    const holiday = holidays.find(
      (h) => h.date === todayIso && holidayAppliesToPerson(h.region, homeCity),
    );
    if (!holiday) return [];
    return [
      {
        category: HOLIDAY_CATEGORY,
        entry: {
          key: `holiday-${person.id}`,
          person,
          kind: "holiday",
          initial: "H",
          typeLabel: "Bank holiday",
          detail: holiday.name,
          backOn: null,
        },
      },
    ];
  });

  const label = (p: PersonDto) => (p.displayName ?? p.username).toLowerCase();
  return [...absenceEntries, ...holidayEntries]
    .sort(
      (a, b) =>
        a.category - b.category || label(a.entry.person).localeCompare(label(b.entry.person)),
    )
    .map(({ entry }) => entry);
}
