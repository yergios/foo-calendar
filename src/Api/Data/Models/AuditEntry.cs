namespace Api.Data.Models;

/// <summary>What each audited action is called. These end up in the admin page and in
/// whatever anyone greps the database with, so treat renames as breaking.</summary>
public static class AuditActions
{
    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonDeleted = "person.deleted";
    public const string PeopleImported = "people.imported";
    public const string AbsenceCreated = "absence.created";
    public const string AbsenceUpdated = "absence.updated";
    public const string AbsenceDeleted = "absence.deleted";

    // No longer recorded: approvals were removed (docs/decisions/0014). Kept because the
    // log is append-only, so entries written before then still carry them until the
    // retention sweep removes them, and the audit page still filters by them.
    public const string AbsenceApproved = "absence.approved";
    public const string AbsenceRejected = "absence.rejected";

    public const string BankHolidayCreated = "bank-holiday.created";
    public const string BankHolidayUpdated = "bank-holiday.updated";
    public const string BankHolidayDeleted = "bank-holiday.deleted";
    public const string BankHolidaysSynced = "bank-holidays.synced";
}

/// <summary>One thing that happened, kept after whatever it happened to is gone. Rows
/// carry their own CreatedBy/UpdatedBy, which covers "who last touched this" for as long
/// as the row exists; this covers the rest, and deletions in particular, where there is no
/// row left to ask.
///
/// Append-only: nothing in the app updates or deletes an entry, and a person's entries
/// deliberately survive their profile being deleted.</summary>
public sealed class AuditEntry
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>Username of whoever did it, or "system" for the scheduled jobs that have no
    /// signed-in user behind them.</summary>
    public required string? Actor { get; set; }

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public required string? Action { get; set; }

    /// <summary>What it happened to, as it read at the time: a display name, a holiday's
    /// name and region. Denormalized on purpose, since the point is to still make sense
    /// once the thing it names has been deleted.</summary>
    public required string? Target { get; set; }

    /// <summary>The part worth reading afterwards: what changed, how much of it, or what
    /// went with it. Never carries an absence note, which is the author's alone.</summary>
    public string? Details { get; set; }
}
