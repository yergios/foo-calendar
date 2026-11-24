using System.Diagnostics;
using System.Diagnostics.Metrics;

using Api.Data.Models;
using Api.Infrastructure;
using Api.Interfaces;
using Api.Services;

using IntegrationTests.TestSupport;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace IntegrationTests;

/// <summary>
/// The spans and metrics of the holiday sync and the retention sweep, as an
/// <see cref="ActivityListener"/> and a <see cref="MeterListener"/> see them: what an
/// OpenTelemetry exporter would be handed.
///
/// The app's source and meter are static and other tests run alongside, so each test runs
/// the job under a trace of its own and keeps only what was recorded in it: spans by trace
/// id, and measurements by the activity current when they were recorded.
/// </summary>
public sealed class TelemetryTests
{
    private const string TestSourceName = "FooCalendar.TelemetryTests";

    private static readonly ActivitySource TestSource = new(TestSourceName);

    /// <summary>One holiday for any country, except that fetching Spain always fails.
    /// </summary>
    private sealed class SpainFailingSource : IHolidaySource
    {
        public HolidaySource Kind => HolidaySource.File;

        public Task<IReadOnlyList<SourceHoliday>> GetAsync(
            string countryCode, int year, CancellationToken ct = default) =>
            countryCode == "ES"
                ? throw new HttpRequestException("The source is down.")
                : Task.FromResult<IReadOnlyList<SourceHoliday>>(
                    [new SourceHoliday(countryCode, new DateOnly(year, 8, 1), "National Day")]);

        public Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetRegionsAsync(string countryCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>Syncs any country until it reaches Germany, where whoever asked for the
    /// sync gives up, the way the host does as it shuts down.</summary>
    private sealed class CancelledAtGermanySource(CancellationTokenSource caller) : IHolidaySource
    {
        public HolidaySource Kind => HolidaySource.File;

        public Task<IReadOnlyList<SourceHoliday>> GetAsync(
            string countryCode, int year, CancellationToken ct = default)
        {
            if (countryCode == "DE")
            {
                caller.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            return Task.FromResult<IReadOnlyList<SourceHoliday>>(
                [new SourceHoliday(countryCode, new DateOnly(year, 8, 1), "National Day")]);
        }

        public Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetRegionsAsync(string countryCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>The app with the source above, or <paramref name="source"/>, and no
    /// background jobs, so only what a test runs is recorded, and nothing deletes or syncs
    /// behind its back.</summary>
    private static TestApp App(params (string Key, string Value)[] settings) => App(null, settings);

    private static TestApp App(IHolidaySource? source, params (string Key, string Value)[] settings) =>
        new([("Holidays:Source", "File"), ("Holidays:File:Path", "unused.json"), .. settings])
        {
            ServiceOverrides = services =>
            {
                if (source is null) services.AddSingleton<IHolidaySource, SpainFailingSource>();
                else services.AddSingleton(source);
                foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService)
                        && (d.ImplementationType == typeof(BankHolidaySyncBackgroundService)
                            || d.ImplementationType == typeof(RetentionBackgroundService))).ToList())
                    services.Remove(job);
            },
        };

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Attributes);

    /// <summary>Everything the app recorded while <paramref name="run"/> ran under a trace
    /// of the test's own.</summary>
    private static async Task<(List<Activity> Spans, List<Measurement> Measurements)> RecordAsync(Func<Task> run)
    {
        var spans = new List<Activity>();
        var measurements = new List<Measurement>();
        ActivityTraceId trace = default;

        using var activities = new ActivityListener
        {
            // By name: TestSource may be created after the listener, and it is offered to the
            // listener from inside its own constructor, before the field holds it.
            ShouldListenTo = source => source.Name is AppTelemetry.Name or TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace && activity.Source.Name == AppTelemetry.Name)
                    lock (spans) spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(activities);

        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AppTelemetry.Name) listener.EnableMeasurementEvents(instrument);
            },
        };
        void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            if (Activity.Current?.TraceId != trace) return;
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags) copy[tag.Key] = tag.Value;
            lock (measurements) measurements.Add(new(instrument.Name, Convert.ToDouble(value, null), copy));
        }
        meters.SetMeasurementEventCallback<long>(Record);
        meters.SetMeasurementEventCallback<double>(Record);
        meters.Start();

        using (var root = TestSource.StartActivity("test").ShouldNotBeNull())
        {
            trace = root.TraceId;
            await run();
        }

        return (spans, measurements);
    }

    private static Dictionary<string, object?> Tags(Activity span) =>
        span.TagObjects.ToDictionary(t => t.Key, t => t.Value);

    private static async Task<IReadOnlyList<string>> SyncAsync(
        TestApp app, IReadOnlyCollection<string> regions, int year, CancellationToken ct = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBankHolidayService>()
            .SyncRegionsAsync(regions, year, ct);
    }

    private static async Task SweepAsync(TestApp app, CancellationToken ct = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRetentionService>().SweepAsync(ct);
    }

    [Fact]
    [Trait("Spec", "OBS-1")]
    [Trait("Spec", "OBS-3")]
    public async Task ASuccessfulSync_RecordsARunSpan_ACountrySpanPerCountry_AndItsMetrics()
    {
        using var app = App();
        _ = app.Services;

        var (spans, measurements) = await RecordAsync(() => SyncAsync(app, ["CH-ZH", "CH-BE", "DE-BE"], 2026));

        var run = spans.Single(s => s.OperationName == AppTelemetry.HolidaySyncSpan);
        Tags(run).ShouldBe(new Dictionary<string, object?>
        {
            [AppTelemetry.YearAttribute] = 2026,
            [AppTelemetry.RegionCountAttribute] = 3,
            [AppTelemetry.FailedCountryCountAttribute] = 0,
        }, ignoreOrder: true);

        var countries = spans.Where(s => s.OperationName == AppTelemetry.HolidaySyncCountrySpan).ToList();
        countries.ShouldAllBe(s => s.ParentSpanId == run.SpanId);
        // One per country, however many of its regions were asked for.
        countries.Select(s => string.Join(", ", Tags(s).OrderBy(t => t.Key, StringComparer.Ordinal)))
            .ShouldBe(
            [
                $"[{AppTelemetry.CountryAttribute}, CH], [{AppTelemetry.OutcomeAttribute}, {AppTelemetry.OutcomeSynced}]",
                $"[{AppTelemetry.CountryAttribute}, DE], [{AppTelemetry.OutcomeAttribute}, {AppTelemetry.OutcomeSynced}]",
            ], ignoreOrder: true);
        countries.ShouldAllBe(s => s.Status == ActivityStatusCode.Unset);

        measurements.Single(m => m.Instrument == "foo_calendar.holiday_sync.runs").Value.ShouldBe(1);
        measurements.Single(m => m.Instrument == "foo_calendar.holiday_sync.duration").Value.ShouldBeGreaterThan(0);
        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.holiday_sync.failed_countries");
    }

    [Fact]
    [Trait("Spec", "OBS-1")]
    [Trait("Spec", "OBS-3")]
    public async Task AFailedCountry_IsAnErrorSpan_AndCountedByItsCode()
    {
        using var app = App();
        _ = app.Services;

        var (spans, measurements) = await RecordAsync(() => SyncAsync(app, ["CH-ZH", "ES-MD", "ES-CT"], 2027));

        var run = spans.Single(s => s.OperationName == AppTelemetry.HolidaySyncSpan);
        Tags(run)[AppTelemetry.FailedCountryCountAttribute].ShouldBe(1);

        var spain = spans.Single(s => s.OperationName == AppTelemetry.HolidaySyncCountrySpan
            && Equals(s.GetTagItem(AppTelemetry.CountryAttribute), "ES"));
        spain.Status.ShouldBe(ActivityStatusCode.Error);
        // The exception's type, never its message.
        Tags(spain).ShouldBe(new Dictionary<string, object?>
        {
            [AppTelemetry.CountryAttribute] = "ES",
            [AppTelemetry.OutcomeAttribute] = AppTelemetry.OutcomeFailed,
            ["error.type"] = typeof(HttpRequestException).FullName,
        }, ignoreOrder: true);
        spain.StatusDescription.ShouldBeNull();

        var failed = measurements.Single(m => m.Instrument == "foo_calendar.holiday_sync.failed_countries");
        failed.Value.ShouldBe(1);
        failed.Attributes.ShouldBe(new Dictionary<string, object?> { [AppTelemetry.CountryAttribute] = "ES" });
        measurements.Single(m => m.Instrument == "foo_calendar.holiday_sync.runs").Value.ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "OBS-2")]
    [Trait("Spec", "OBS-3")]
    public async Task ASweepThatDeletesRows_RecordsASpan_AndTheRowsDeletedPerTable()
    {
        using var app = App(("Retention:AuditEntryDays", "30"), ("Retention:AbsenceDays", "30"));
        app.Seed(db =>
        {
            var person = Rows.Person("jdoe");
            db.People.Add(person);
            foreach (var daysAgo in (int[])[40, 50, 60, 1])
                db.AuditEntries.Add(new AuditEntry
                {
                    OccurredAt = DateTime.UtcNow.AddDays(-daysAgo),
                    Actor = "jdoe",
                    Action = AuditActions.AbsenceCreated,
                    Target = "Jane Doe",
                });
            var longAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
            db.Absences.Add(Rows.Vacation(person, longAgo, longAgo.AddDays(2)));
            db.Absences.Add(Rows.Vacation(person, longAgo.AddDays(10), longAgo.AddDays(12)));
        });

        var (spans, measurements) = await RecordAsync(() => SweepAsync(app));

        Tags(spans.Single(s => s.OperationName == AppTelemetry.RetentionSweepSpan)).ShouldBe(
            new Dictionary<string, object?>
            {
                [AppTelemetry.AuditEntriesDeletedAttribute] = 3,
                [AppTelemetry.AbsencesDeletedAttribute] = 2,
            }, ignoreOrder: true);

        measurements.Where(m => m.Instrument == "foo_calendar.retention.deleted_rows")
            .ToDictionary(m => m.Attributes[AppTelemetry.TableAttribute]!, m => m.Value)
            .ShouldBe(new Dictionary<object, double>
            {
                [AppTelemetry.AuditEntriesTable] = 3,
                [AppTelemetry.AbsencesTable] = 2,
            }, ignoreOrder: true);
    }

    // Shutdown is not a failing source: nothing is counted as failed, nothing is an error,
    // and the run is not counted as one that completed.
    [Fact]
    [Trait("Spec", "OBS-1")]
    public async Task ASyncCancelledPartWay_IsCancelled_NotAFailedCountry()
    {
        using var caller = new CancellationTokenSource();
        using var app = App(new CancelledAtGermanySource(caller));
        _ = app.Services;

        var (spans, measurements) = await RecordAsync(() =>
            Should.ThrowAsync<OperationCanceledException>(
                () => SyncAsync(app, ["CH-ZH", "DE-BE", "FR-IDF"], 2026, caller.Token)));

        var run = spans.Single(s => s.OperationName == AppTelemetry.HolidaySyncSpan);
        Tags(run)[AppTelemetry.OutcomeAttribute].ShouldBe(AppTelemetry.OutcomeCancelled);
        Tags(run)[AppTelemetry.FailedCountryCountAttribute].ShouldBe(0);
        run.Status.ShouldBe(ActivityStatusCode.Unset);

        var germany = spans.Single(s => s.OperationName == AppTelemetry.HolidaySyncCountrySpan
            && Equals(s.GetTagItem(AppTelemetry.CountryAttribute), "DE"));
        Tags(germany)[AppTelemetry.OutcomeAttribute].ShouldBe(AppTelemetry.OutcomeCancelled);
        germany.Status.ShouldBe(ActivityStatusCode.Unset);
        Tags(germany).ShouldNotContainKey(AppTelemetry.ErrorTypeAttribute);
        // The run stopped there.
        spans.ShouldNotContain(s => Equals(s.GetTagItem(AppTelemetry.CountryAttribute), "FR"));

        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.holiday_sync.failed_countries");
        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.holiday_sync.runs");
        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.holiday_sync.duration");
    }

    // A run takes anything from milliseconds, for a file, to minutes, for a slow upstream
    // and many countries. The SDK's default boundaries are for milliseconds and would lump
    // all of it together.
    [Fact]
    [Trait("Spec", "OBS-1")]
    public void TheSyncDuration_IsBucketedInSeconds()
    {
        var buckets = AppTelemetry.HolidaySyncDuration.Advice?.HistogramBucketBoundaries;

        buckets.ShouldNotBeNull();
        buckets.ShouldBe(AppTelemetry.HolidaySyncDurationBuckets);
        buckets.ShouldBeInOrder(SortDirection.Ascending);
        buckets[0].ShouldBeLessThanOrEqualTo(0.1);
        buckets[^1].ShouldBeGreaterThanOrEqualTo(300);
        AppTelemetry.HolidaySyncDuration.Unit.ShouldBe("s");
    }

    [Fact]
    [Trait("Spec", "OBS-2")]
    [Trait("Spec", "OBS-3")]
    public async Task AFailedSweep_IsAnErrorSpan_AndCounted()
    {
        using var app = App(("Retention:AuditEntryDays", "30"));
        // Whatever goes wrong underneath, here a table that is not there.
        app.Seed(db => db.Database.ExecuteSqlRaw("DROP TABLE AuditEntries"));

        var (spans, measurements) = await RecordAsync(() =>
            Should.ThrowAsync<SqliteException>(() => SweepAsync(app)));

        var sweep = spans.Single(s => s.OperationName == AppTelemetry.RetentionSweepSpan);
        sweep.Status.ShouldBe(ActivityStatusCode.Error);
        sweep.StatusDescription.ShouldBeNull();
        Tags(sweep).ShouldBe(new Dictionary<string, object?>
        {
            [AppTelemetry.ErrorTypeAttribute] = typeof(SqliteException).FullName,
        });

        measurements.Single(m => m.Instrument == "foo_calendar.retention.failed_sweeps").Value.ShouldBe(1);
        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.retention.deleted_rows");
    }

    [Fact]
    [Trait("Spec", "OBS-2")]
    public async Task ASweepStoppedByShutdown_IsNotAFailure()
    {
        using var app = App(("Retention:AuditEntryDays", "30"));
        _ = app.Services;
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        var (spans, measurements) = await RecordAsync(() =>
            Should.ThrowAsync<OperationCanceledException>(() => SweepAsync(app, shutdown.Token)));

        spans.Single(s => s.OperationName == AppTelemetry.RetentionSweepSpan)
            .Status.ShouldBe(ActivityStatusCode.Unset);
        measurements.ShouldNotContain(m => m.Instrument == "foo_calendar.retention.failed_sweeps");
    }
}
