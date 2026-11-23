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

/** One person off today, and why. A person with several reasons is still one entry, shown
 * as the reason that sorts first. */
export interface OffTodayEntry {
  key: string;
  person: PersonDto;
  kind: "absence" | "holiday";
  /** The absence type's letter, or "H" for a bank holiday. */
  initial: string;
  typeLabel: string;
  /** Every time range of their time-based absences today, or the holiday's name, then the
   * return date when a whole-day absence has them out. */
  detail: string;
  /** The first later working day they are back, when a whole-day absence has them out. */
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

/** Everything that has one person off today, gathered before any entry is built so that
 * each person yields exactly one. */
interface Reasons {
  person: PersonDto;
  timeBased: AbsenceDto[];
  /** ABS-5 leaves at most one whole-day absence on a day, and none beside a time-based one. */
  wholeDay: AbsenceDto | null;
  holiday: BankHolidayDto | null;
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

// One entry for one person, shown as the reason that sorts first: time ranges when they
// have any, otherwise the holiday, otherwise the whole-day absence. The return date goes
// with it whenever a whole-day absence has them out, holiday or not, since that is the
// date someone looking at the list actually needs.
function entryFor(
  todayIso: string,
  { person, timeBased, wholeDay, holiday }: Reasons,
  bookedOff: Map<number, Set<string>>,
  holidays: readonly BankHolidayDto[],
): Ranked {
  const backOn = wholeDay
    ? nextWorkingDay(
        wholeDay.endDate,
        unavailableCheckFor(person, bookedOff.get(person.id), holidays),
      )
    : null;
  // The year is worth spelling out once the return date crosses into the next one:
  // "02/01" on its own reads as a date that has already been and gone.
  const backOnDetail =
    backOn === null
      ? null
      : `Back on the ${formatDayMonth(backOn, backOn.slice(0, 4) !== todayIso.slice(0, 4))}`;

  const ranges = [...timeBased].sort((a, b) =>
    `${a.startTime}${a.endTime}`.localeCompare(`${b.startTime}${b.endTime}`),
  );
  const earliest = ranges[0];

  let category: number;
  let reason: Pick<OffTodayEntry, "kind" | "initial" | "typeLabel"> & { detail: string | null };
  if (earliest) {
    category = TIME_BASED_CATEGORY;
    // Lettered by the earliest: the stack is usually one type, and the times that follow
    // are what tells several apart.
    reason = {
      kind: "absence",
      initial: ABSENCE_TYPE_INITIALS[earliest.type],
      typeLabel: earliest.type,
      detail: ranges.map((a) => formatTimeRange(a.startTime, a.endTime)).join(", "),
    };
  } else if (holiday) {
    category = HOLIDAY_CATEGORY;
    reason = { kind: "holiday", initial: "H", typeLabel: "Bank holiday", detail: holiday.name };
  } else {
    // Reasons are only gathered for someone with at least one of the three.
    const absence = wholeDay!;
    category = WHOLE_DAY_CATEGORY[absence.type];
    reason = {
      kind: "absence",
      initial: ABSENCE_TYPE_INITIALS[absence.type],
      typeLabel: absence.type,
      detail: null,
    };
  }

  return {
    category,
    entry: {
      key: `person-${person.id}`,
      person,
      kind: reason.kind,
      initial: reason.initial,
      typeLabel: reason.typeLabel,
      detail: [reason.detail, backOnDetail].filter((part) => part !== null).join(" · "),
      backOn,
    },
  };
}

/**
 * Everyone off today, each person once: anyone with an absence covering today or a bank
 * holiday of their own today. Ordered time-based absences first, then holidays, then
 * whole-day absences by type, and by name within each.
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

  const reasonsById = new Map<number, Reasons>();
  const reasonsOf = (person: PersonDto): Reasons => {
    let reasons = reasonsById.get(person.id);
    if (reasons === undefined) {
      reasons = { person, timeBased: [], wholeDay: null, holiday: null };
      reasonsById.set(person.id, reasons);
    }
    return reasons;
  };

  for (const absence of absences) {
    // The absences reach forward for the "back on" dates, so an absence starting next month
    // must not put anyone on today's list.
    if (absence.startDate > todayIso || absence.endDate < todayIso) continue;
    const person = peopleById.get(absence.personId);
    if (!person) continue;
    const reasons = reasonsOf(person);
    if (absence.startTime) reasons.timeBased.push(absence);
    else reasons.wholeDay ??= absence;
  }

  for (const person of people) {
    const homeCity = person.homeCity;
    if (!homeCity) continue;
    // The holidays reach past today for the "back on" dates, so a holiday three weeks out
    // must not put anyone on today's list.
    const holiday = holidays.find(
      (h) => h.date === todayIso && holidayAppliesToPerson(h.region, homeCity),
    );
    if (holiday) reasonsOf(person).holiday = holiday;
  }

  const label = (p: PersonDto) => (p.displayName ?? p.username).toLowerCase();
  return [...reasonsById.values()]
    .map((reasons) => entryFor(todayIso, reasons, bookedOff, holidays))
    .sort(
      (a, b) =>
        a.category - b.category || label(a.entry.person).localeCompare(label(b.entry.person)),
    )
    .map(({ entry }) => entry);
}
