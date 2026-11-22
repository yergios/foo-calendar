namespace Api.Configuration;

/// <summary>How long the app keeps data that has outlived its purpose.
///
/// There is no "GDPR number" to put here: Article 5(1)(e) says personal data must be kept
/// no longer than is necessary for the purpose it was collected for, and the purpose is
/// the deploying team's to state, so the periods below are defaults to be justified rather
/// than a compliance guarantee. See docs/data-protection.md.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Value of any of the periods below that switches it off, keeping the rows
    /// indefinitely.</summary>
    public const int Never = 0;

    /// <summary>How often the sweep runs. Daily: these are slow-moving thresholds, and a
    /// deletion arriving within a day of its due date is well inside any period worth
    /// configuring.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>Days an audit entry is kept after it was recorded. A year by default: long
    /// enough to answer "who deleted that, and when" a season or two later, short enough
    /// that the log doesn't quietly become a permanent record of everyone who ever worked
    /// here. Audit entries name people who may since have been deleted from the app
    /// entirely (see AuditEntry), so this is the only thing bounding how long those names
    /// survive their own profile.</summary>
    public int AuditEntryDays { get; init; } = 365;

    /// <summary>Days an absence is kept after it ended. Off by default, and deliberately:
    /// looking back over previous years is what a leave calendar is *for*, so a default
    /// that quietly deleted last year's absences would be deleting the product. A team
    /// that has decided it doesn't need history beyond, say, three years can set 1095.</summary>
    public int AbsenceDays { get; init; } = Never;
}
