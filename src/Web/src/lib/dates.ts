// Plain functions, not a composable: nothing here is reactive, so wrapping them in a
// factory would only add a closure allocation per component for no benefit.

/** Parses a "yyyy-MM-dd" string as a date in the browser's local timezone. */
export function parse(isoDate: string): Date {
  const parts = isoDate.split("-");
  return new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
}

/** Formats a date as "yyyy-MM-dd" in the browser's local timezone. */
export function toIso(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

export function today(): string {
  return toIso(new Date());
}

/** Native <input type="time"> gives back "HH:mm" once a user has touched it (or "HH:mm:ss"
 * if left at its seeded value), but the API's TimeOnly JSON round-trip is "HH:mm:ss". */
export function toApiTime(hhmm: string): string {
  return hhmm.length === 5 ? `${hhmm}:00` : hhmm;
}

/** The reverse of toApiTime: the API's "HH:mm:ss" trimmed to "HH:mm" for display, or "" for
 * a whole-day absence's null start/end time. */
export function toDisplayTime(time: string | null | undefined): string {
  return time?.slice(0, 5) ?? "";
}

const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

function ordinal(n: number): string {
  const rem100 = n % 100;
  if (rem100 >= 11 && rem100 <= 13) return `${n}th`;
  switch (n % 10) {
    case 1: return `${n}st`;
    case 2: return `${n}nd`;
    case 3: return `${n}rd`;
    default: return `${n}th`;
  }
}

/** A locale-independent long date, e.g. "Monday 8th of August, 2026". Deliberately not
 * Intl.DateTimeFormat, whose exact wording and even weekday/month spelling depend on the
 * browser's locale and ICU data. Scoped to the time-based absence UI; the calendar grid's
 * own month/weekday headers keep using Intl.DateTimeFormat(navigator.language, ...). */
export function formatLongDate(isoDate: string): string {
  const date = parse(isoDate);
  return `${WEEKDAYS[date.getDay()]} ${ordinal(date.getDate())} of ${MONTHS[date.getMonth()]}, ${date.getFullYear()}`;
}
