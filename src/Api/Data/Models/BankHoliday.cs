namespace Api.Data.Models;

public enum HolidaySource { Manual, Nager }

public sealed class BankHoliday
{
    public int Id { get; set; }
    public required string Region { get; set; }   // ISO code, e.g. "CH", "CH-ZH"
    public required DateOnly Date { get; set; }
    public required string Name { get; set; }
    public HolidaySource Source { get; set; }

    // When true, Date's month/day recurs every year (past and future) instead of applying
    // only to the specific year it was stored with. Only ever set on manually-added rows.
    public bool IsRecurringAnnually { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Username of whoever added or last touched this row. Nager-sourced rows use a fixed
    // "nager-sync" marker instead of a real username, since nobody manually added those.
    public required string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }
}
