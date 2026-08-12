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
