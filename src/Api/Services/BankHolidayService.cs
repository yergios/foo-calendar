using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class BankHolidayService(
    AppDbContext db,
    HttpClient http,
    ILogger<BankHolidayService> logger) : IBankHolidayService
{
    private sealed record NagerHoliday(
        DateOnly Date,
        string LocalName,
        string Name,
        string CountryCode,
        bool Global,
        string[] Counties,
        string[] Types);

    // This is the one place that defines how a subdivision code maps to its country.
    // Keep both derivations below pointed at it, so they can't quietly drift apart.
    private const string CountrySeparator = "-";

    // Marks rows the Nager sync itself created or refreshed, since there's no real
    // username behind those.
    private const string SyncActor = "nager-sync";

    private static string CountryOf(string region) =>
        region.Contains(CountrySeparator, StringComparison.Ordinal) ? region.Split(CountrySeparator)[0] : region;

    public async Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        // Expand each region to itself + its country code, so "ES-MD" also pulls in nationwide "ES" rows.
        var expanded = regions
            .SelectMany(r => new[] { r, CountryOf(r) })
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
                (h, year) => (Holiday: h, Date: RecurringOccurrenceIn(h, year)))
            .Where(x => x.Date is DateOnly d && d >= from && d <= to)
            .Select(x => BankHolidayDto.FromEntity(x.Holiday) with { Date = x.Date!.Value });

        var holidays = oneTime.Concat(recurring).OrderBy(h => h.Date).ToList();

        return new(holidays);
    }

    // Feb 29 only recurs on leap years; every other stored month/day recurs every year.
    private static DateOnly? RecurringOccurrenceIn(BankHoliday holiday, int year) =>
        holiday.Date.Month == 2 && holiday.Date.Day == 29 && !DateTime.IsLeapYear(year)
            ? null
            : new DateOnly(year, holiday.Date.Month, holiday.Date.Day);

    // Two holidays in the same region collide if they'd ever land on the same calendar day.
    // A recurring one recurs by month/day regardless of the year it happens to be stored
    // with, so comparing raw dates isn't enough once either side is recurring.
    private static bool Collides(DateOnly existingDate, bool existingRecurring, DateOnly date, bool recurring) =>
        existingRecurring || recurring
            ? existingDate.Month == date.Month && existingDate.Day == date.Day
            : existingDate == date;

    private async Task SyncCountryAsync(string countryCode, int year, CancellationToken ct = default)
    {
        var holidays = await http.GetFromJsonAsync<List<NagerHoliday>>(
            $"PublicHolidays/{year}/{countryCode}", ct);

        if (holidays is null) return;

        // Load everything already in the DB for this country. Tracked, so updates are cheap.
        // This stays an inline predicate instead of calling CountryOf(), so it still runs as
        // one SQL WHERE clause instead of pulling every row to the client.
        var existing = await db.BankHolidays
            .Where(b => b.Region == countryCode || b.Region.StartsWith(countryCode + CountrySeparator))
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
            // Global (nationwide): one row under the country code.
            // Not global: one row per county it applies to.
            var regions = h.Global ? [countryCode] : h.Counties ?? [];

            foreach (var r in regions)
            {
                var current = FindExisting(r, h.Date);

                if (current is not null)
                {
                    if (current.Source == HolidaySource.Manual)
                        continue; // never clobber a manual override, recurring or not

                    current.Name = h.Name; // refresh name on re-sync
                    current.UpdatedAt = DateTime.UtcNow;
                    current.UpdatedBy = SyncActor;
                    continue;
                }

                if (!seen.Add((r, h.Date)))
                    continue; // duplicate within this same Nager response, first one wins

                db.BankHolidays.Add(new BankHoliday
                {
                    Region = r,
                    Date = h.Date,
                    Name = h.Name,
                    Source = HolidaySource.Nager,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = SyncActor,
                });
            }
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
