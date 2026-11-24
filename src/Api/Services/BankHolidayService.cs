using System.Data;
using System.Diagnostics;

using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Infrastructure;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class BankHolidayService(
    AppDbContext db,
    IHolidaySource source,
    IAuditLog audit,
    ILogger<BankHolidayService> logger) : IBankHolidayService
{
    // Manual holidays are everyone's to keep up to date, which is exactly why every hand
    // edit is logged: nobody has to guess who moved a day the whole team plans around.
    private static string Describe(BankHoliday holiday) =>
        $"{holiday.Region}, {holiday.Date:yyyy-MM-dd}"
        + (holiday.IsRecurringAnnually ? ", every year" : "");


    // Marks rows the sync itself created or refreshed, since there's no real username
    // behind those. Named after the source so it stays obvious which system put a row
    // there after a deployment switches sources. Spelled out rather than derived from the
    // enum: these strings are persisted in audit columns, so they shouldn't change shape
    // just because an enum member gets renamed.
    private readonly string syncActor = source.Kind switch
    {
        HolidaySource.Nager => "nager-sync",
        HolidaySource.File => "file-sync",
        _ => "sync",
    };

    public async Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        // Expand each region to itself + its country code, so "ES-MD" also pulls in nationwide "ES" rows.
        List<string> expanded = [.. regions.SelectMany(RegionCode.SelfAndCountry).Distinct()];

        // Recurring rows are rare, so they're the only ones fetched unfiltered by date (there's
        // no SQL-side way to test "does this month/day fall in range" without pulling it in the
        // first place). Everything else stays a plain, indexed, date-bounded SQL query.
        var oneTimeEntities = await db.BankHolidays
            .Where(h => expanded.Contains(h.Region) && !h.IsRecurringAnnually && h.Date >= from && h.Date <= to)
            .ToListAsync(ct);

        var recurringEntities = await db.BankHolidays
            .Where(h => expanded.Contains(h.Region) && h.IsRecurringAnnually)
            .ToListAsync(ct);

        var oneTime = oneTimeEntities.Select(BankHolidayDto.FromEntity);

        // A recurring holiday isn't stored per year, so project its stored month/day onto
        // every year the requested range touches, and keep only the occurrences that
        // actually land inside [from, to].
        var recurring = recurringEntities
            .SelectMany(h => Enumerable.Range(from.Year, to.Year - from.Year + 1)
                .Select(year => BankHoliday.OccurrenceIn(h.Date, year))
                .OfType<DateOnly>()
                .Where(d => d >= from && d <= to)
                .Select(d => BankHolidayDto.FromEntity(h) with { Date = d }));

        var holidays = oneTime.Concat(recurring).OrderBy(h => h.Date).ToList();

        return new(holidays);
    }

    // Two holidays in the same region collide if they'd ever land on the same calendar day.
    // A recurring one recurs by month/day regardless of the year it happens to be stored
    // with, so comparing raw dates isn't enough once either side is recurring.
    private static bool Collides(DateOnly existingDate, bool existingRecurring, DateOnly date, bool recurring) =>
        existingRecurring || recurring
            ? existingDate.Month == date.Month && existingDate.Day == date.Day
            : existingDate == date;

    private async Task SyncCountryAsync(
        string countryCode,
        IReadOnlyCollection<string> regions,
        int year,
        CancellationToken ct = default)
    {
        var fetched = await source.GetAsync(countryCode, year, ct);

        // Sources answer per country and hand back every subdivision they know of, so this
        // is where the team's actual regions come in: keep the nationwide rows, keep the
        // subdivisions somebody lives in, drop the rest rather than storing holidays for
        // places the calendar will never show.
        var holidays = fetched
            .Where(h => h.Region == countryCode || regions.Contains(h.Region))
            .ToList();

        if (holidays.Count == 0) return;

        // Load everything already in the DB for this country. Tracked, so updates are cheap.
        // This stays an inline predicate instead of calling RegionCode.BelongsTo(), which EF
        // can't translate, so it still runs as one SQL WHERE clause instead of pulling every
        // row to the client.
        var existing = await db.BankHolidays
            .Where(b => b.Region == countryCode
                || b.Region.StartsWith(countryCode + RegionCode.Separator))
            .ToListAsync(ct);

        // The overwhelming majority of rows are exact-date, non-recurring, so they get an O(1)
        // dictionary lookup. Only the rare recurring override needs a month/day scan, and that
        // scan stays limited to the handful of rows actually flagged recurring.
        var exact = existing
            .Where(b => !b.IsRecurringAnnually)
            .ToDictionary(b => (b.Region, b.Date));
        var recurring = existing.Where(b => b.IsRecurringAnnually).ToList();

        BankHoliday? FindExisting(string region, DateOnly date)
        {
            if (exact.TryGetValue((region, date), out var match)) return match;
            return recurring.FirstOrDefault(b => b.Region == region && Collides(b.Date, true, date, false));
        }

        // Tracks (region, date) pairs already handled during this call, whether pre-existing
        // or just queued, so a second Nager entry landing on the same day gets skipped.
        var seen = new HashSet<(string Region, DateOnly Date)>(existing.Select(b => (b.Region, b.Date)));

        foreach (var h in holidays)
        {
            var current = FindExisting(h.Region, h.Date);

            if (current is not null)
            {
                if (current.Source == HolidaySource.Manual)
                    continue; // never clobber a manual override, recurring or not

                current.Name = h.Name; // refresh name on re-sync
                // A deployment can switch sources; whichever one maintains the row now is
                // the one it should be labelled with.
                current.Source = source.Kind;
                current.UpdatedAt = DateTime.UtcNow;
                current.UpdatedBy = syncActor;
                continue;
            }

            if (!seen.Add((h.Region, h.Date)))
                continue; // duplicate within this same response, first one wins

            db.BankHolidays.Add(new BankHoliday
            {
                Region = h.Region,
                Date = h.Date,
                Name = h.Name,
                Source = source.Kind,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = syncActor,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SyncRegionsAsync(
        IReadOnlyCollection<string> regions, int year, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        using var run = AppTelemetry.ActivitySource.StartActivity(AppTelemetry.HolidaySyncSpan);
        run?.SetTag(AppTelemetry.YearAttribute, year);
        run?.SetTag(AppTelemetry.RegionCountAttribute, regions.Count);

        var failed = new List<string>();
        var failedCountries = 0;

        // One fetch per country, however many of its regions are in play: a source has no
        // narrower unit than a country, so asking per region would just repeat the same
        // request.
        foreach (var country in regions.GroupBy(RegionCode.CountryOf, StringComparer.Ordinal))
        {
            using var span = AppTelemetry.ActivitySource.StartActivity(AppTelemetry.HolidaySyncCountrySpan);
            span?.SetTag(AppTelemetry.CountryAttribute, country.Key);

            try
            {
                await SyncCountryAsync(country.Key, [.. country], year, ct);
                span?.SetTag(AppTelemetry.OutcomeAttribute, AppTelemetry.OutcomeSynced);
            }
            // The host shutting down, or the caller giving up: the run stops here, and
            // nothing about this country failed. An HTTP timeout is also a cancellation, but
            // not of this token, so it is a failure like any other below.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                span?.SetTag(AppTelemetry.OutcomeAttribute, AppTelemetry.OutcomeCancelled);
                run?.SetTag(AppTelemetry.OutcomeAttribute, AppTelemetry.OutcomeCancelled);
                run?.SetTag(AppTelemetry.FailedCountryCountAttribute, failedCountries);
                throw;
            }
#pragma warning disable CA1031 // Catch-all on purpose: a failed country is reported in failedRegions, and must not stop the rest syncing.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // one country failing shouldn't block the rest
                logger.LogWarning(ex, "Bank holiday sync failed for {Country} {Year}", country.Key, year);
                failed.AddRange(country);
                failedCountries++;

                // The exception's type and not its message (OBS-3). The log has the rest.
                span?.SetTag(AppTelemetry.OutcomeAttribute, AppTelemetry.OutcomeFailed);
                span?.SetTag(AppTelemetry.ErrorTypeAttribute, ex.GetType().FullName);
                span?.SetStatus(ActivityStatusCode.Error);
                AppTelemetry.HolidaySyncFailedCountries.Add(
                    1, new KeyValuePair<string, object?>(AppTelemetry.CountryAttribute, country.Key));
            }
        }

        // A cancelled run left above, so these count completed runs only: a run that never
        // finished has no duration worth comparing.
        run?.SetTag(AppTelemetry.FailedCountryCountAttribute, failedCountries);
        AppTelemetry.HolidaySyncRuns.Add(1);
        AppTelemetry.HolidaySyncDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);

        return failed;
    }

    public async Task<int> RemoveForRegionsAsync(
        IReadOnlyCollection<string> regions, CancellationToken ct = default)
    {
        if (regions.Count == 0) return 0;

        // One DELETE rather than loading rows to delete them: nothing here needs the
        // entities, and a region being abandoned can involve years of accumulated rows.
        return await db.BankHolidays
            .Where(b => regions.Contains(b.Region))
            .ExecuteDeleteAsync(ct);
    }

    // Shared by both add and edit: loads only the two columns collision detection actually
    // needs, instead of pulling every column of every same-region row.
    private async Task EnsureNoCollisionAsync(
        string region, DateOnly date, bool recurring, int? excludeId, CancellationToken ct)
    {
        var candidates = await db.BankHolidays
            .Where(b => b.Region == region && b.Id != excludeId)
            .Select(b => new { b.Date, b.IsRecurringAnnually })
            .ToListAsync(ct);

        if (candidates.Any(b => Collides(b.Date, b.IsRecurringAnnually, date, recurring)))
            throw new ConflictException($"A holiday already exists for {region} on {date}.");
    }

    // Fair game is anywhere the team is, plus the countries those places are in, since a
    // nationwide holiday applies to everyone in the country. Anywhere else would store a
    // row that no calendar query would ever ask for.
    private static void EnsureTracked(IReadOnlyCollection<string> trackedRegions, string region)
    {
        if (trackedRegions.Contains(region)) return;
        if (trackedRegions.Any(r => RegionCode.CountryOf(r) == region)) return;

        // Invalid input, not a conflict: the request is wrong whatever is stored, so it is
        // a 400 rather than a 409.
        throw new InvalidInputException(
            $"No one on the team is in {region}, so a bank holiday can't be added for it.");
    }

    public async Task<BankHolidayDto> AddManualAsync(
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default)
    {
        EnsureTracked(trackedRegions, request.Region);

        var holiday = new BankHoliday
        {
            Region = request.Region,
            Date = request.Date,
            Name = request.Name,
            Source = HolidaySource.Manual,
            IsRecurringAnnually = request.IsRecurringAnnually,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorUsername,
        };

        // Serializable: same reasoning as AbsenceService's overlap checks. SQLite takes the
        // write lock the instant this transaction opens, so a second, colliding add just
        // waits for this one to commit before running its own collision check.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await EnsureNoCollisionAsync(request.Region, request.Date, request.IsRecurringAnnually, excludeId: null, ct);

            db.BankHolidays.Add(holiday);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await audit.RecordAsync(AuditActions.BankHolidayCreated, holiday.Name, Describe(holiday), ct);

        return BankHolidayDto.FromEntity(holiday);
    }

    public async Task<BankHolidayDto> UpdateManualAsync(
        int id,
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default)
    {
        var holiday = await db.BankHolidays.FindAsync([id], ct)
            ?? throw new NotFoundException($"Bank holiday {id} not found.");

        if (holiday.Source != HolidaySource.Manual)
            throw new ConflictException("Only manually added holidays can be edited.");

        EnsureTracked(trackedRegions, request.Region);

        var before = $"{holiday.Name} ({Describe(holiday)})";

        // Serializable, same reasoning as AddManualAsync: guards the collision check
        // against a concurrent add/edit landing a colliding holiday before this commits.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await EnsureNoCollisionAsync(request.Region, request.Date, request.IsRecurringAnnually, excludeId: id, ct);

            holiday.Region = request.Region;
            holiday.Date = request.Date;
            holiday.Name = request.Name;
            holiday.IsRecurringAnnually = request.IsRecurringAnnually;
            holiday.UpdatedAt = DateTime.UtcNow;
            holiday.UpdatedBy = actorUsername;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await audit.RecordAsync(
            AuditActions.BankHolidayUpdated,
            holiday.Name,
            $"{before} → {holiday.Name} ({Describe(holiday)})",
            ct);

        return BankHolidayDto.FromEntity(holiday);
    }

    public async Task DeleteManualAsync(int id, CancellationToken ct = default)
    {
        var holiday = await db.BankHolidays.FindAsync([id], ct)
            ?? throw new NotFoundException($"Bank holiday {id} not found.");

        if (holiday.Source != HolidaySource.Manual)
            throw new ConflictException("Only manually added holidays can be deleted.");

        db.BankHolidays.Remove(holiday);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditActions.BankHolidayDeleted, holiday.Name, Describe(holiday), ct);
    }
}
