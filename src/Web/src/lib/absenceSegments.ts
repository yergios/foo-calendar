import { formatTimeRange } from "@/lib/dates";
import { sameList } from "@/lib/reconcile";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

/** A nationwide holiday (no subdivision suffix) covers everyone in the country; a
 * subdivision's own holiday covers only people set to that exact subdivision, not
 * siblings under the same country. Mirrors the API's RegionCode.BelongsTo. */
export function holidayAppliesToPerson(holidayRegion: string, personHomeCity: string): boolean {
  if (holidayRegion === personHomeCity) return true;
  const holidayIsNationwide = !holidayRegion.includes("-");
  const personCountry = personHomeCity.split("-")[0] ?? personHomeCity;
  return holidayIsNationwide && personCountry === holidayRegion;
}

const NO_HOLIDAYS: ReadonlyMap<string, readonly BankHolidayDto[]> = new Map();

// Whether a day is a bank holiday for this specific person - the day it's off *as a
// holiday*, not as a personal absence. A person with no region set has none: there's
// nothing to match a holiday's region against.
function isRegionalHoliday(
  iso: string,
  personHomeCity: string | null,
  holidaysByIso: ReadonlyMap<string, readonly BankHolidayDto[]>,
): boolean {
  if (!personHomeCity) return false;
  const entries = holidaysByIso.get(iso);
  if (!entries) return false;
  return entries.some((h) => holidayAppliesToPerson(h.region, personHomeCity));
}

/** The one thing a day contributes to segmenting: whether an absence paints over it. */
export interface SegmentDay {
  iso: string;
  isWeekend: boolean;
}

/**
 * A contiguous run of days that render identically for one person: the same absences and
 * the same painted/unpainted state. This is what the calendar grid renders one element
 * for, instead of one cell per day: a person with fifty absences a year is a few dozen
 * segments, not 365 cells.
 *
 * `start`/`end` are inclusive indices into the days array the segments were built from.
 */
export interface AbsenceSegment {
  start: number;
  end: number;
  /** Every absence touching these days - own or not, tooltips are visible to everyone. */
  absences: AbsenceDto[];
  /** Whether the run shows the person's color. A weekend isn't a work day, so an absence
   * stretch doesn't paint over it. Neither does a bank holiday that applies to the
   * person's own region (nationwide, or that exact subdivision): the cell is a holiday,
   * not a personal day, and painting over it would hide that. A bank holiday that doesn't
   * apply to them is a normal working day as far as they're concerned, so an absence still
   * paints over it, same as always. */
  painted: boolean;
  /** The legend letter for a painted run, shown once per run rather than once per day; "" on
   * an unpainted one. */
  label: string;
}

/** Maps each day's ISO date to its index in the array, built once per year. */
export function dayIndexOf(days: readonly SegmentDay[]): Map<string, number> {
  return new Map(days.map((day, index) => [day.iso, index]));
}

// The letter the legend shows, with a trailing "?" for anything still awaiting review, so a
// pending absence reads as "color, but unconfirmed" without needing a second visual language
// on top of the one the calendar already has.
//
// A day holds at most one whole-day absence (mutual exclusivity is enforced server-side),
// which then owns the letter. Otherwise it holds only time-based ones, any number of which
// can stack: pending if any one of them is, and the letter comes from the first entry's own
// type - the stack is usually one type, and on the rare day it isn't (e.g. an Appointment
// and a time-based Other), the tooltip is what disambiguates the rest.
function labelFor(dayAbsences: AbsenceDto[]): string {
  const wholeDay = dayAbsences.find((a) => !a.startTime);
  if (wholeDay) {
    const initial = ABSENCE_TYPE_INITIALS[wholeDay.type];
    return wholeDay.status === "Pending" ? `${initial}?` : initial;
  }
  const initial = ABSENCE_TYPE_INITIALS[dayAbsences[0]!.type];
  return dayAbsences.some((a) => a.status === "Pending") ? `${initial}?` : initial;
}

/**
 * One person's absences, expanded onto the year's days and merged into runs. Two adjacent
 * days join a run when they hold exactly the same absences and paint the same way, so a
 * five-day vacation is one painted run labelled "V" once, the weekend inside a longer one is
 * its own unpainted run (still carrying the absence for its tooltip), and a time-based
 * absence, being single-day, always ends up in a run of its own.
 *
 * Absences are clamped to the year: the API returns anything overlapping the range, so a
 * stretch can start before January 1 or end after December 31.
 *
 * `personHomeCity` and `holidaysByIso` decide which days are *that person's* bank
 * holidays (see `painted` on AbsenceSegment) - both optional so callers that don't care
 * about the distinction, like most of the test suite, can ignore it.
 */
export function segmentsForPerson(
  absences: readonly AbsenceDto[],
  days: readonly SegmentDay[],
  dayIndex: Map<string, number>,
  personHomeCity: string | null = null,
  holidaysByIso: ReadonlyMap<string, readonly BankHolidayDto[]> = NO_HOLIDAYS,
): AbsenceSegment[] {
  if (days.length === 0 || absences.length === 0) return [];
  const firstIso = days[0]!.iso;
  const lastIso = days[days.length - 1]!.iso;

  // Sparse: only days touched by an absence get an array, everything else stays undefined.
  const byDay = new Array<AbsenceDto[] | undefined>(days.length);
  for (const absence of absences) {
    const start = absence.startDate < firstIso ? 0 : dayIndex.get(absence.startDate);
    const end = absence.endDate > lastIso ? days.length - 1 : dayIndex.get(absence.endDate);
    if (start === undefined || end === undefined) continue;
    for (let i = start; i <= end; i++) {
      const list = byDay[i];
      if (list) list.push(absence);
      else byDay[i] = [absence];
    }
  }

  const segments: AbsenceSegment[] = [];
  let current: AbsenceSegment | null = null;
  for (let i = 0; i < days.length; i++) {
    const dayAbsences = byDay[i];
    if (dayAbsences === undefined) {
      current = null;
      continue;
    }
    const painted =
      !days[i]!.isWeekend && !isRegionalHoliday(days[i]!.iso, personHomeCity, holidaysByIso);
    // Every day's list is filled by the same pass in the same absence order, so two days
    // holding the same absences hold them in the same order too.
    if (current && current.painted === painted && sameList(current.absences, dayAbsences)) {
      current.end = i;
      continue;
    }
    current = {
      start: i,
      end: i,
      absences: dayAbsences,
      painted,
      label: painted ? labelFor(dayAbsences) : "",
    };
    segments.push(current);
  }
  return segments;
}

/** The segment covering a day index, if any. Segments are few and ordered, so a linear scan
 * is cheaper than anything smarter. */
export function segmentAt(segments: readonly AbsenceSegment[], dayIdx: number) {
  for (const segment of segments) {
    if (segment.start > dayIdx) return undefined;
    if (segment.end >= dayIdx) return segment;
  }
  return undefined;
}

/**
 * The tooltip for a cell's absences: every absence on it, own or not - tooltips are
 * visible to everyone, only editing is restricted. One line per absence, so several
 * same-day time-based absences each get their own. Used both as the hovered cell's title
 * and as each segment's accessible label.
 */
export function absenceTooltip(
  person: PersonDto,
  absences: readonly AbsenceDto[],
  isMine: boolean,
  homeTimeZone: string | undefined,
): string {
  const name = person.displayName ?? person.username;
  const lines = absences.map((a) => {
    const status = a.status === "Pending" ? " (pending approval)" : "";
    const timeRange = a.startTime ? ` ${formatTimeRange(a.startTime, a.endTime)}` : "";
    const body = a.note ? `${a.type}${timeRange}: ${a.note}` : `${a.type}${timeRange}`;
    return `${name} — ${body}${status}`;
  });
  // Once at the end rather than repeated per line: every time here is in the same zone,
  // same as the add/edit form's own (Europe/Zurich) labels.
  if (homeTimeZone && absences.some((a) => a.startTime)) lines.push(`(times in ${homeTimeZone})`);
  if (isMine) lines.push("(click to edit)");
  return lines.join("\n");
}
