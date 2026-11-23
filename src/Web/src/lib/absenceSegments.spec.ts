import { describe, expect, it } from "vitest";

import { addDays, toIso } from "@/lib/dates";
import { type Gen, forAll } from "@/lib/propertyTesting";
import { ABSENCE_TYPE_INITIALS, type AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";

import {
  absenceTooltip,
  dayIndexOf,
  holidayAppliesToPerson,
  segmentAt,
  segmentsForPerson,
} from "./absenceSegments";

// January 2026: the 1st is a Thursday, so the 3rd/4th, 10th/11th, ... are weekends.
const days = Array.from({ length: 31 }, (_, i) => {
  const d = new Date(2026, 0, 1 + i);
  return { iso: toIso(d), isWeekend: d.getDay() === 0 || d.getDay() === 6 };
});
const dayIndex = dayIndexOf(days);

let nextId = 1;
function absence(
  startDate: string,
  endDate: string,
  overrides: Partial<AbsenceDto> = {},
): AbsenceDto {
  return {
    id: nextId++,
    personId: 1,
    startDate,
    endDate,
    type: "Vacation",
    note: null,
    createdAt: "2026-01-01T00:00:00",
    startTime: null,
    endTime: null,
    ...overrides,
  };
}

const segments = (...absences: AbsenceDto[]) => segmentsForPerson(absences, days, dayIndex);

let nextHolidayId = 1;
function holiday(date: string, region: string): BankHolidayDto {
  return {
    id: nextHolidayId++,
    region,
    date,
    name: "Test holiday",
    source: "Manual",
    isRecurringAnnually: false,
  };
}

function holidaysByIso(...holidays: BankHolidayDto[]): Map<string, BankHolidayDto[]> {
  const map = new Map<string, BankHolidayDto[]>();
  for (const h of holidays) {
    const entries = map.get(h.date) ?? [];
    entries.push(h);
    map.set(h.date, entries);
  }
  return map;
}

describe("segmentsForPerson", () => {
  it("[UI-2] returns nothing for a person without absences", () => {
    expect(segments()).toEqual([]);
  });

  it("[UI-2] merges a workweek vacation into one painted run labelled once", () => {
    const v = absence("2026-01-05", "2026-01-09");
    expect(segments(v)).toEqual([{ start: 4, end: 8, absences: [v], painted: true, label: "V" }]);
  });

  it("[UI-2] splits a run at the weekend, which stays unpainted but keeps the absence", () => {
    const v = absence("2026-01-08", "2026-01-13");
    const result = segments(v);
    expect(result.map((s) => [s.start, s.end, s.painted, s.label])).toEqual([
      [7, 8, true, "V"],
      [9, 10, false, ""],
      [11, 12, true, "V"],
    ]);
    expect(result[1]!.absences).toEqual([v]);
  });

  it("[UI-2] keeps two different whole-day absences apart even when adjacent", () => {
    const v = absence("2026-01-05", "2026-01-06");
    const s = absence("2026-01-07", "2026-01-08", { type: "Other" });
    expect(segments(v, s).map((x) => [x.start, x.end, x.label])).toEqual([
      [4, 5, "V"],
      [6, 7, "O"],
    ]);
  });

  it("[UI-2] never joins time-based absences into a run and labels each day", () => {
    const a = absence("2026-01-05", "2026-01-05", {
      type: "Appointment",
      startTime: "09:00:00",
      endTime: "10:00:00",
    });
    const b = absence("2026-01-06", "2026-01-06", {
      type: "Appointment",
      startTime: "11:00:00",
      endTime: "12:00:00",
    });
    expect(segments(a, b).map((s) => [s.start, s.end, s.label, s.absences])).toEqual([
      [4, 4, "A", [a]],
      [5, 5, "A", [b]],
    ]);
  });

  it("[UI-2] stacks same-day time-based absences into one segment lettered by the first", () => {
    const a = absence("2026-01-05", "2026-01-05", {
      type: "Appointment",
      startTime: "09:00:00",
      endTime: "10:00:00",
    });
    const b = absence("2026-01-05", "2026-01-05", {
      type: "Other",
      startTime: "14:00:00",
      endTime: "15:00:00",
    });
    expect(segments(a, b)).toEqual([
      { start: 4, end: 4, absences: [a, b], painted: true, label: "A" },
    ]);
  });

  it("[UI-2] clamps absences that overlap the edges of the year", () => {
    const before = absence("2025-12-29", "2026-01-02");
    const after = absence("2026-01-30", "2026-02-03");
    // January 30 is a Friday, so the 31st is its own unpainted run.
    expect(segments(before, after).map((s) => [s.start, s.end, s.painted])).toEqual([
      [0, 1, true],
      [29, 29, true],
      [30, 30, false],
    ]);
  });

  it("[UI-2] ignores absences entirely outside the year", () => {
    expect(segments(absence("2025-12-01", "2025-12-05"))).toEqual([]);
  });

  it("[UI-2] [HOL-9] splits a run at a bank holiday matching the person's exact region, leaving it unpainted", () => {
    const v = absence("2026-01-05", "2026-01-09");
    // Jan 6 is a regional holiday for CH-ZH.
    const holidays = holidaysByIso(holiday("2026-01-06", "CH-ZH"));
    const result = segmentsForPerson([v], days, dayIndex, "CH-ZH", holidays);
    expect(result.map((s) => [s.start, s.end, s.painted])).toEqual([
      [4, 4, true], // Jan 5
      [5, 5, false], // Jan 6, the holiday
      [6, 8, true], // Jan 7-9
    ]);
  });

  it("[UI-2] [HOL-9] splits a run at a nationwide bank holiday covering the person's country", () => {
    const v = absence("2026-01-05", "2026-01-09");
    const holidays = holidaysByIso(holiday("2026-01-06", "CH"));
    const result = segmentsForPerson([v], days, dayIndex, "CH-ZH", holidays);
    expect(result.map((s) => [s.start, s.end, s.painted])).toEqual([
      [4, 4, true],
      [5, 5, false],
      [6, 8, true],
    ]);
  });

  it("[UI-2] [HOL-9] keeps a run painted through a bank holiday for a different region entirely", () => {
    const v = absence("2026-01-05", "2026-01-09");
    const holidays = holidaysByIso(holiday("2026-01-06", "ES-MD"));
    const result = segmentsForPerson([v], days, dayIndex, "CH-ZH", holidays);
    expect(result).toEqual([{ start: 4, end: 8, absences: [v], painted: true, label: "V" }]);
  });

  it("[UI-2] [HOL-9] keeps a run painted through a bank holiday for a sibling region in the same country", () => {
    const v = absence("2026-01-05", "2026-01-09");
    const holidays = holidaysByIso(holiday("2026-01-06", "CH-BE"));
    const result = segmentsForPerson([v], days, dayIndex, "CH-ZH", holidays);
    expect(result).toEqual([{ start: 4, end: 8, absences: [v], painted: true, label: "V" }]);
  });

  it("[UI-2] [HOL-9] keeps a run painted through a bank holiday when the person has no region set", () => {
    const v = absence("2026-01-05", "2026-01-09");
    const holidays = holidaysByIso(holiday("2026-01-06", "CH-ZH"));
    const result = segmentsForPerson([v], days, dayIndex, null, holidays);
    expect(result).toEqual([{ start: 4, end: 8, absences: [v], painted: true, label: "V" }]);
  });
});

// What a person's row looks like for a year, built the way the API allows it: whole-day
// absences share no day with any other absence, time-based ones stack only with each other.
interface SegmentsCase {
  year: number;
  homeCity: string | null;
  absences: AbsenceDto[];
  holidays: BankHolidayDto[];
}

function yearDays(year: number) {
  const result = [];
  for (const d = new Date(year, 0, 1); d.getFullYear() === year; d.setDate(d.getDate() + 1))
    result.push({ iso: toIso(d), isWeekend: d.getDay() === 0 || d.getDay() === 6 });
  return result;
}

function generateSegmentsCase(gen: Gen): SegmentsCase {
  const year = gen.between(2024, 2028);
  // From a fortnight before the year, so some absences have to be clamped at either end.
  const anyDay = () => addDays(`${year - 1}-12-18`, gen.between(0, 380));
  const overlaps = (a: AbsenceDto, b: AbsenceDto) =>
    a.startDate <= b.endDate && b.startDate <= a.endDate;

  const absences: AbsenceDto[] = [];
  for (let attempt = gen.between(0, 12); attempt > 0; attempt--) {
    const startDate = anyDay();
    const timeBased = gen.chance(0.3);
    const candidate = absence(
      startDate,
      timeBased ? startDate : addDays(startDate, gen.between(0, 12)),
      timeBased
        ? {
            type: gen.pick(["Appointment", "Other"] as const),
            startTime: "09:00:00",
            endTime: "10:00:00",
          }
        : { type: gen.pick(["Vacation", "Other"] as const) },
    );
    const allowed = absences.every(
      (a) => !overlaps(a, candidate) || (a.startTime !== null && candidate.startTime !== null),
    );
    if (allowed) absences.push(candidate);
  }

  const holidays = gen.list(0, 25, (g) =>
    holiday(anyDay(), g.pick(["ES", "ES-MD", "ES-CT", "FR"])),
  );
  return { year, homeCity: gen.pick(["ES-MD", "ES", null]), absences, holidays };
}

describe("segmentsForPerson, against painting day by day", () => {
  it("[UI-2] [HOL-9] partitions the absent days into maximal runs that paint like each day", () => {
    forAll(300, generateSegmentsCase, ({ year, homeCity, absences, holidays }) => {
      const daysOfYear = yearDays(year);
      const result = segmentsForPerson(
        absences,
        daysOfYear,
        dayIndexOf(daysOfYear),
        homeCity,
        holidaysByIso(...holidays),
      );

      // The person's own holidays: their country's nationwide ones and their exact region's.
      const country = homeCity?.split("-")[0];
      const isOwnHoliday = (iso: string) =>
        holidays.some((h) => h.date === iso && (h.region === homeCity || h.region === country));

      const segmentOf = new Array<(typeof result)[number] | undefined>(daysOfYear.length);
      let previousEnd = -1;
      for (const segment of result) {
        expect(segment.start).toBeGreaterThan(previousEnd);
        expect(segment.end).toBeGreaterThanOrEqual(segment.start);
        for (let i = segment.start; i <= segment.end; i++) segmentOf[i] = segment;
        previousEnd = segment.end;
      }

      daysOfYear.forEach((day, i) => {
        const covering = absences.filter((a) => a.startDate <= day.iso && day.iso <= a.endDate);
        const segment = segmentOf[i];
        if (covering.length === 0) {
          expect(segment, day.iso).toBeUndefined();
          return;
        }
        const painted = !day.isWeekend && !isOwnHoliday(day.iso);
        const owner = covering.find((a) => a.startTime === null) ?? covering[0]!;
        expect(segment, day.iso).toBeDefined();
        expect(
          segment!.absences.map((a) => a.id),
          day.iso,
        ).toEqual(covering.map((a) => a.id));
        expect(segment!.painted, day.iso).toBe(painted);
        expect(segment!.label, day.iso).toBe(painted ? ABSENCE_TYPE_INITIALS[owner.type] : "");
      });

      // Maximal: two runs that touch differ in what they hold or how they paint.
      for (let k = 1; k < result.length; k++) {
        const [before, after] = [result[k - 1]!, result[k]!];
        if (before.end + 1 !== after.start) continue;
        const same =
          before.painted === after.painted &&
          before.absences.map((a) => a.id).join() === after.absences.map((a) => a.id).join();
        expect(same, `runs ending ${before.end} and starting ${after.start}`).toBe(false);
      }
    });
  });
});

describe("holidayAppliesToPerson", () => {
  it("[HOL-9] matches a holiday for the person's exact region", () => {
    expect(holidayAppliesToPerson("CH-ZH", "CH-ZH")).toBe(true);
  });

  it("[HOL-9] matches a nationwide holiday against anyone in that country", () => {
    expect(holidayAppliesToPerson("CH", "CH-ZH")).toBe(true);
  });

  it("[HOL-9] does not match a different country's holiday", () => {
    expect(holidayAppliesToPerson("ES", "CH-ZH")).toBe(false);
  });

  it("[HOL-9] does not match a sibling region's own holiday", () => {
    expect(holidayAppliesToPerson("CH-BE", "CH-ZH")).toBe(false);
  });

  it("[HOL-9] does not let a region-specific holiday apply to the bare country", () => {
    expect(holidayAppliesToPerson("CH-ZH", "CH")).toBe(false);
  });
});

describe("segmentAt", () => {
  it("[UI-2] finds the segment covering a day and nothing between segments", () => {
    const result = segments(
      absence("2026-01-05", "2026-01-06"),
      absence("2026-01-08", "2026-01-08"),
    );
    expect(segmentAt(result, 5)).toBe(result[0]);
    expect(segmentAt(result, 6)).toBeUndefined();
    expect(segmentAt(result, 7)).toBe(result[1]);
    expect(segmentAt(result, 20)).toBeUndefined();
  });
});

describe("absenceTooltip", () => {
  const person = { id: 1, username: "jdoe", displayName: "Jane Doe", homeCity: null };

  it("[UI-3] lists every absence on the cell, time ranges labelled with the team's zone", () => {
    const appointment = absence("2026-01-05", "2026-01-05", {
      type: "Appointment",
      startTime: "09:00:00",
      endTime: "10:30:00",
    });
    const other = absence("2026-01-05", "2026-01-05", {
      type: "Other",
      startTime: "14:00:00",
      endTime: "15:00:00",
      note: "Errand",
    });

    expect(absenceTooltip(person, [appointment, other], false, "Europe/Zurich")).toBe(
      [
        "Jane Doe — Appointment 09:00–10:30",
        "Jane Doe — Other 14:00–15:00: Errand",
        "(times in Europe/Zurich)",
      ].join("\n"),
    );
  });

  it("[UI-3] names no zone for whole-day absences and offers editing only on one's own", () => {
    const vacation = absence("2026-01-05", "2026-01-09");

    expect(absenceTooltip(person, [vacation], true, "Europe/Zurich")).toBe(
      "Jane Doe — Vacation\n(click to edit)",
    );
  });
});
