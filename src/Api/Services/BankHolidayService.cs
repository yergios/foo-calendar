using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class BankHolidayService(
    AppDbContext db,
    IHolidaySource source,
    ILogger<BankHolidayService> logger) : IBankHolidayService
{
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
        var expanded = regions
            .SelectMany(r => new[] { r, RegionCode.CountryOf(r) })
            .Distinct()
            .ToList();

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
            .SelectMany(
                h => Enumerable.Range(from.Year, to.Year - from.Year + 1),
                (h, year) => (Holiday: h, Date: BankHoliday.OccurrenceIn(h.Date, year)))
            .Where(x => x.Date is DateOnly d && d >= from && d <= to)
            .Select(x => BankHolidayDto.FromEntity(x.Holiday) with { Date = x.Date!.Value });

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

    private async Task SyncCountryAsync(string countryCode, int year, CancellationToken ct = default)
    {
        var holidays = await source.GetAsync(countryCode, year, ct);

        if (holidays.Count == 0) return;

        // Load everything already in the DB for this country. Tracked, so updates are cheap.
        // This stays an inline predicate instead of calling RegionCode.BelongsTo(), which EF
        // can't translate, so it still runs as one SQL WHERE clause instead of pulling every
        // row to the client.
        var existing = await db.BankHolidays
            .Where(b => b.Region == countryCode || b.Region.StartsWith(countryCode + RegionCode.Separator))
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

    public async Task<IReadOnlyList<string>> SyncCountriesAsync(
        IEnumerable<string> countryCodes, int year, CancellationToken ct = default)
    {
        var failed = new List<string>();

        foreach (var countryCode in countryCodes)
        {
            try
            {
                await SyncCountryAsync(countryCode, year, ct);
            }
            catch (Exception ex)
            {
                // one country failing shouldn't block the rest
                logger.LogWarning(ex, "Bank holiday sync failed for {Country} {Year}", countryCode, year);
                failed.Add(countryCode);
            }
        }

        return failed;
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
            throw new InvalidOperationException($"A holiday already exists for {region} on {date}.");
    }

    public async Task<BankHolidayDto> AddManualAsync(
        AddManualHolidayRequest request, string actorUsername, CancellationToken ct = default)
    {
        await EnsureNoCollisionAsync(request.Region, request.Date, request.IsRecurringAnnually, excludeId: null, ct);

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

        db.BankHolidays.Add(holiday);
        await db.SaveChangesAsync(ct);
        return BankHolidayDto.FromEntity(holiday);
    }

    public async Task<BankHolidayDto> UpdateManualAsync(
        int id, AddManualHolidayRequest request, string actorUsername, CancellationToken ct = default)
    {
        var holiday = await db.BankHolidays.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Bank holiday {id} not found.");

        if (holiday.Source != HolidaySource.Manual)
            throw new InvalidOperationException("Only manually added holidays can be edited.");

        await EnsureNoCollisionAsync(request.Region, request.Date, request.IsRecurringAnnually, excludeId: id, ct);

        holiday.Region = request.Region;
        holiday.Date = request.Date;
        holiday.Name = request.Name;
        holiday.IsRecurringAnnually = request.IsRecurringAnnually;
        holiday.UpdatedAt = DateTime.UtcNow;
        holiday.UpdatedBy = actorUsername;
        await db.SaveChangesAsync(ct);

        return BankHolidayDto.FromEntity(holiday);
    }

    public async Task DeleteManualAsync(int id, CancellationToken ct = default)
    {
        var holiday = await db.BankHolidays.FindAsync([id], ct)
            ?? throw new KeyNotFoundException($"Bank holiday {id} not found.");

        if (holiday.Source != HolidaySource.Manual)
            throw new InvalidOperationException("Only manually added holidays can be deleted.");

        db.BankHolidays.Remove(holiday);
        await db.SaveChangesAsync(ct);
    }
}
