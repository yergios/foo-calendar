using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Api.Infrastructure;

/// <summary>
/// The app's own spans and metrics, for the work nobody watches happen: the holiday sync and
/// the retention sweep run in the background, and without these a failing country or a
/// sweep that stopped deleting shows up only as a log line, if anyone reads it. Program.cs
/// registers both with OpenTelemetry under <see cref="Name"/>.
///
/// Attributes carry codes, counts and durations only, never a username, a name or a note
/// (OBS-3): whatever is added here leaves the process for any collector configured.
/// </summary>
public static class AppTelemetry
{
    /// <summary>The name of both the activity source and the meter.</summary>
    public const string Name = "FooCalendar";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter Meter = new(Name);

    // ---- Holiday sync ----------------------------------------------------------------

    /// <summary>Span of one sync run: one year, every region asked for.</summary>
    public const string HolidaySyncSpan = "holiday_sync";

    /// <summary>Span of one country within a run, a child of <see cref="HolidaySyncSpan"/>.</summary>
    public const string HolidaySyncCountrySpan = "holiday_sync.country";

    public const string YearAttribute = "foo_calendar.holiday_sync.year";
    public const string RegionCountAttribute = "foo_calendar.holiday_sync.region_count";
    public const string FailedCountryCountAttribute = "foo_calendar.holiday_sync.failed_country_count";
    public const string CountryAttribute = "foo_calendar.holiday_sync.country";
    public const string OutcomeAttribute = "foo_calendar.holiday_sync.outcome";

    public const string OutcomeSynced = "synced";
    public const string OutcomeFailed = "failed";

    /// <summary>Stopped by the host shutting down, or by the request that asked for it going
    /// away: neither the source's fault nor the country's, so not counted as a failure.
    /// </summary>
    public const string OutcomeCancelled = "cancelled";

    /// <summary>The exception's type on a failed span, the OpenTelemetry convention. Never
    /// its message, which is the source's or the database's text and could carry anything
    /// (OBS-3).</summary>
    public const string ErrorTypeAttribute = "error.type";

    public static readonly Counter<long> HolidaySyncRuns = Meter.CreateCounter<long>(
        "foo_calendar.holiday_sync.runs", "{run}", "Holiday sync runs, one per year synced.");

    public static readonly Counter<long> HolidaySyncFailedCountries = Meter.CreateCounter<long>(
        "foo_calendar.holiday_sync.failed_countries", "{country}",
        "Countries whose holidays could not be fetched or stored, tagged by country.");

    /// <summary>Bucket boundaries in seconds. The SDK's defaults are made for milliseconds
    /// (0, 5, 10, 25, ... 10000), which would put every run, from a file source's few
    /// milliseconds to a slow upstream's minutes, into the first two buckets.</summary>
    public static readonly IReadOnlyList<double> HolidaySyncDurationBuckets =
        [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300];

    public static readonly Histogram<double> HolidaySyncDuration = Meter.CreateHistogram(
        "foo_calendar.holiday_sync.duration", "s", "How long one holiday sync run took.",
        tags: null,
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = HolidaySyncDurationBuckets });

    // ---- Retention sweep -------------------------------------------------------------

    /// <summary>Span of one retention sweep.</summary>
    public const string RetentionSweepSpan = "retention_sweep";

    public const string TableAttribute = "foo_calendar.retention.table";
    public const string AuditEntriesDeletedAttribute = "foo_calendar.retention.audit_entries_deleted";
    public const string AbsencesDeletedAttribute = "foo_calendar.retention.absences_deleted";

    public const string AuditEntriesTable = "audit_entries";
    public const string AbsencesTable = "absences";

    public static readonly Counter<long> RetentionDeletedRows = Meter.CreateCounter<long>(
        "foo_calendar.retention.deleted_rows", "{row}",
        "Rows the retention sweep deleted, tagged by table.");

    public static readonly Counter<long> RetentionFailedSweeps = Meter.CreateCounter<long>(
        "foo_calendar.retention.failed_sweeps", "{sweep}",
        "Retention sweeps that failed, so data was kept past its retention period.");
}
