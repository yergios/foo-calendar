import { describe, expect, it } from "vitest";

import { toIso } from "@/lib/dates";
import type { AbsenceDto } from "@/types/absence";

import { dayIndexOf, segmentAt, segmentsForPerson } from "./absenceSegments";

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
    status: "Approved",
    note: null,
    createdAt: "2026-01-01T00:00:00",
    startTime: null,
    endTime: null,
    ...overrides,
  };
}

const segments = (...absences: AbsenceDto[]) => segmentsForPerson(absences, days, dayIndex);

describe("segmentsForPerson", () => {
  it("returns nothing for a person without absences", () => {
    expect(segments()).toEqual([]);
  });

  it("merges a workweek vacation into one painted run labelled once", () => {
    const v = absence("2026-01-05", "2026-01-09");
    expect(segments(v)).toEqual([{ start: 4, end: 8, absences: [v], painted: true, label: "V" }]);
  });

  it("splits a run at the weekend, which stays unpainted but keeps the absence", () => {
    const v = absence("2026-01-08", "2026-01-13");
    const result = segments(v);
    expect(result.map((s) => [s.start, s.end, s.painted, s.label])).toEqual([
      [7, 8, true, "V"],
      [9, 10, false, ""],
      [11, 12, true, "V"],
    ]);
    expect(result[1]!.absences).toEqual([v]);
  });

  it("keeps two different whole-day absences apart even when adjacent", () => {
    const v = absence("2026-01-05", "2026-01-06");
    const s = absence("2026-01-07", "2026-01-08", { type: "Other" });
    expect(segments(v, s).map((x) => [x.start, x.end, x.label])).toEqual([
      [4, 5, "V"],
      [6, 7, "O"],
    ]);
  });

  it("never joins time-based absences into a run and labels each day", () => {
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

  it("stacks same-day time-based absences into one segment, pending if any is", () => {
    const a = absence("2026-01-05", "2026-01-05", {
      type: "Appointment",
      startTime: "09:00:00",
      endTime: "10:00:00",
    });
    const b = absence("2026-01-05", "2026-01-05", {
      type: "Other",
      startTime: "14:00:00",
      endTime: "15:00:00",
      status: "Pending",
    });
    expect(segments(a, b)).toEqual([
      { start: 4, end: 4, absences: [a, b], painted: true, label: "A?" },
    ]);
  });

  it("marks a pending whole-day absence with a question mark", () => {
    expect(segments(absence("2026-01-05", "2026-01-05", { status: "Pending" }))[0]!.label).toBe(
      "V?",
    );
  });

  it("clamps absences that overlap the edges of the year", () => {
    const before = absence("2025-12-29", "2026-01-02");
    const after = absence("2026-01-30", "2026-02-03");
    // January 30 is a Friday, so the 31st is its own unpainted run.
    expect(segments(before, after).map((s) => [s.start, s.end, s.painted])).toEqual([
      [0, 1, true],
      [29, 29, true],
      [30, 30, false],
    ]);
  });

  it("ignores absences entirely outside the year", () => {
    expect(segments(absence("2025-12-01", "2025-12-05"))).toEqual([]);
  });
});

describe("segmentAt", () => {
  it("finds the segment covering a day and nothing between segments", () => {
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
