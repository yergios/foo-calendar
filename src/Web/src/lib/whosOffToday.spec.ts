import { describe, expect, it } from "vitest";

import type { AbsenceDto } from "@/types/absence";
import type { BankHolidayDto } from "@/types/bankHoliday";
import type { PersonDto } from "@/types/person";

import { whosOffToday } from "./whosOffToday";

// Wednesday 12 August 2026.
const TODAY = "2026-08-12";

function person(id: number, displayName: string, homeCity: string | null = "CH-ZH"): PersonDto {
  return { id, username: displayName.toLowerCase(), displayName, homeCity };
}

let nextId = 1;
function absence(
  owner: PersonDto,
  startDate: string,
  endDate: string,
  overrides: Partial<AbsenceDto> = {},
): AbsenceDto {
  return {
    id: nextId++,
    personId: owner.id,
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

function appointment(
  owner: PersonDto,
  date: string,
  startTime = "09:00:00",
  endTime = "10:30:00",
): AbsenceDto {
  return absence(owner, date, date, { type: "Appointment", startTime, endTime });
}

let nextHolidayId = 1;
function holiday(date: string, region: string, name = "Holiday"): BankHolidayDto {
  return { id: nextHolidayId++, region, date, name, source: "Manual", isRecurringAnnually: false };
}

const ana = person(1, "Ana");
const ben = person(2, "Ben");
const cleo = person(3, "cleo");
const dan = person(4, "Dan", null);

describe("whosOffToday", () => {
  it("[UI-5] lists an absence covering today, and none that ended or has yet to start", () => {
    const entries = whosOffToday(
      TODAY,
      [ana, ben, cleo],
      [
        absence(ana, "2026-08-10", "2026-08-12"),
        absence(ben, "2026-08-03", "2026-08-11"),
        absence(cleo, "2026-08-13", "2026-08-14"),
      ],
      [],
    );

    expect(entries.map((e) => e.person.displayName)).toEqual(["Ana"]);
  });

  it("[UI-5] [HOL-9] lists a holiday of the person's own today, nationwide or their exact region", () => {
    const entries = whosOffToday(
      TODAY,
      [person(1, "Zurich", "CH-ZH"), person(2, "Bern", "CH-BE"), person(3, "Swiss", "CH"), dan],
      [],
      [holiday(TODAY, "CH-ZH", "Knabenschiessen")],
    );

    expect(entries).toEqual([
      expect.objectContaining({
        kind: "holiday",
        initial: "H",
        typeLabel: "Bank holiday",
        detail: "Knabenschiessen",
      }),
    ]);
    expect(entries[0]!.person.displayName).toBe("Zurich");

    const nationwide = whosOffToday(
      TODAY,
      [person(1, "Zurich", "CH-ZH"), person(2, "Swiss", "CH"), dan],
      [],
      [holiday(TODAY, "CH")],
    );
    expect(nationwide.map((e) => e.person.displayName)).toEqual(["Swiss", "Zurich"]);
  });

  it("[UI-5] lists each person once, with every time range of their appointments", () => {
    const entries = whosOffToday(
      TODAY,
      [ana],
      [appointment(ana, TODAY, "14:00:00", "15:00:00"), appointment(ana, TODAY)],
      [],
    );

    expect(entries).toEqual([
      expect.objectContaining({
        person: ana,
        kind: "absence",
        initial: "A",
        detail: "09:00–10:30, 14:00–15:00",
        backOn: null,
      }),
    ]);
  });

  it("[UI-5] lists each person once, for their appointment rather than also their holiday", () => {
    const entries = whosOffToday(TODAY, [ana], [appointment(ana, TODAY)], [holiday(TODAY, "CH")]);

    expect(entries).toEqual([
      expect.objectContaining({ kind: "absence", initial: "A", detail: "09:00–10:30" }),
    ]);
  });

  it("[UI-5] lists each person once, as their holiday with the return date of their absence", () => {
    const entries = whosOffToday(
      TODAY,
      [ana, ben],
      [absence(ana, "2026-08-10", "2026-08-14"), absence(ben, TODAY, TODAY)],
      [holiday(TODAY, "CH", "Knabenschiessen")],
    );

    expect(entries).toEqual([
      expect.objectContaining({
        person: ana,
        kind: "holiday",
        initial: "H",
        typeLabel: "Bank holiday",
        detail: "Knabenschiessen · Back on the 17/08",
        backOn: "2026-08-17",
      }),
      expect.objectContaining({
        person: ben,
        kind: "holiday",
        detail: "Knabenschiessen · Back on the 13/08",
        backOn: "2026-08-13",
      }),
    ]);
  });

  it("[UI-5] orders time-based absences first, then holidays, then Vacation, then Other, by name", () => {
    const eve = person(5, "Eve", "FR");
    const entries = whosOffToday(
      TODAY,
      [ana, ben, cleo, dan, eve],
      [
        absence(ana, TODAY, TODAY, { type: "Other" }),
        absence(ben, TODAY, TODAY),
        appointment(cleo, TODAY),
        absence(dan, TODAY, TODAY),
      ],
      [holiday(TODAY, "FR")],
    );

    expect(entries.map((e) => [e.person.displayName, e.typeLabel])).toEqual([
      ["cleo", "Appointment"],
      ["Eve", "Bank holiday"],
      ["Ben", "Vacation"],
      ["Dan", "Vacation"],
      ["Ana", "Other"],
    ]);
  });

  it("[UI-5] gives a time-based absence its times and no return date", () => {
    const [entry] = whosOffToday(TODAY, [ana], [appointment(ana, TODAY)], []);

    expect(entry).toMatchObject({ initial: "A", detail: "09:00–10:30", backOn: null });
  });

  it("[UI-5] is back on the next weekday after a whole-day absence", () => {
    const [entry] = whosOffToday(TODAY, [ana], [absence(ana, "2026-08-10", "2026-08-14")], []);

    expect(entry).toMatchObject({ backOn: "2026-08-17", detail: "Back on the 17/08" });
  });

  it("[UI-5] [HOL-9] skips the person's own holidays on the way back, and nobody else's", () => {
    const holidays = [holiday("2026-08-17", "CH-ZH"), holiday("2026-08-18", "CH-BE")];

    const [entry] = whosOffToday(
      TODAY,
      [ana],
      [absence(ana, "2026-08-10", "2026-08-14")],
      holidays,
    );

    expect(entry!.backOn).toBe("2026-08-18");
  });

  // The case fixed in 62eb83f: the server rejects overlapping absences, not adjacent ones.
  it("[UI-5] skips absences that follow directly, but not a time-based one", () => {
    const [entry] = whosOffToday(
      TODAY,
      [ana],
      [
        absence(ana, "2026-08-10", "2026-08-14"),
        absence(ana, "2026-08-17", "2026-08-19", { type: "Other" }),
        absence(ana, "2026-08-20", "2026-08-20"),
        appointment(ana, "2026-08-21"),
      ],
      [],
    );

    expect(entry!.backOn).toBe("2026-08-21");
  });

  // The other case fixed in 62eb83f: "01/01" alone reads as a date already past.
  it("[UI-5] spells out the year of a return date in the next one", () => {
    const newYearsEve = "2026-12-31";

    const [plain] = whosOffToday(newYearsEve, [ana], [absence(ana, "2026-12-28", newYearsEve)], []);
    const [afterHoliday] = whosOffToday(
      newYearsEve,
      [ana],
      [absence(ana, "2026-12-28", newYearsEve)],
      [holiday("2027-01-01", "CH")],
    );

    expect(plain).toMatchObject({ backOn: "2027-01-01", detail: "Back on the 01/01/2027" });
    expect(afterHoliday).toMatchObject({ backOn: "2027-01-04", detail: "Back on the 04/01/2027" });
  });

  it("[UI-5] leaves out an absence of someone not on the team list", () => {
    expect(whosOffToday(TODAY, [ana], [absence(ben, TODAY, TODAY)], [])).toEqual([]);
  });
});
