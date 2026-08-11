const CET_LOCALE_FALLBACK = "en-GB"; // day first

export function useDateFormat() {
  function parse(isoDate: string): Date {
    const parts = isoDate.split("-");
    const y = Number(parts[0]);
    const m = Number(parts[1]);
    const d = Number(parts[2]);
    return new Date(y, m - 1, d);
  }

  function toIso(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, "0");
    const d = String(date.getDate()).padStart(2, "0");
    return `${y}-${m}-${d}`;
  }

  function display(isoDate: string, opts?: Intl.DateTimeFormatOptions): string {
    return new Intl.DateTimeFormat(navigator.language || CET_LOCALE_FALLBACK, opts).format(
      parse(isoDate),
    );
  }

  function displayShort(isoDate: string): string {
    return display(isoDate, { day: "2-digit", month: "short" });
  }

  function today(): string {
    return toIso(new Date());
  }

  return { parse, toIso, display, displayShort, today };
}
