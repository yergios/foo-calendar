using Api.Configuration;
using Api.Data;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class TeamRegionService(
    AppDbContext db,
    IBankHolidayService bankHolidays,
    IOptions<TeamOptions> team,
    ILogger<TeamRegionService> logger) : ITeamRegionService
{
    private string HomeRegion => team.Value.HomeRegion;

    public async Task<IReadOnlyList<string>> InUseAsync(CancellationToken ct = default)
    {
        var regions = await db.People
            .Where(p => p.HomeCity != null && p.HomeCity != "")
            .Select(p => p.HomeCity)
            .Distinct()
            .ToListAsync(ct);

        if (!regions.Contains(HomeRegion, StringComparer.Ordinal))
            regions.Add(HomeRegion);

        regions.Sort(StringComparer.Ordinal);
        return regions;
    }

    public async Task EnsureSyncedAsync(string region, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(region)) return;

        // Same two years the background job keeps warm, so a region joined in December
        // isn't blank for whoever is already looking at next year's calendar.
        var year = DateTime.UtcNow.Year;

        try
        {
            var failed = await bankHolidays.SyncRegionsAsync([region], year, ct);
            failed = [.. failed, .. await bankHolidays.SyncRegionsAsync([region], year + 1, ct)];

            if (failed.Count > 0)
                logger.LogWarning(
                    "Bank holidays for {Region} could not be synced after a profile change. " +
                    "The scheduled sync will retry.", region);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately swallowed. Somebody saving their profile shouldn't see it fail
            // because a holiday source is down, and both the scheduled sync and the admin
            // re-sync will pick this up later.
            logger.LogWarning(ex, "Bank holiday sync for {Region} threw after a profile change.", region);
        }
    }

    public async Task PruneIfUnusedAsync(string region, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(region)) return;

        var country = RegionCode.CountryOf(region);
        var homeCountry = RegionCode.CountryOf(HomeRegion);
        var doomed = new HashSet<string>(StringComparer.Ordinal);

        // The subdivision goes when nobody is left in it specifically.
        if (region != HomeRegion && !await db.People.AnyAsync(p => p.HomeCity == region, ct))
            doomed.Add(region);

        // The country's own nationwide rows are a separate question, and a stricter one:
        // they only go once nobody is left anywhere in the country. People in ES-AS still
        // need Spain's national holidays after the last person in ES-MD leaves. Written as
        // an inline predicate rather than RegionCode.BelongsTo() so EF can translate it.
        if (country != homeCountry && !await db.People.AnyAsync(
            p => p.HomeCity == country || p.HomeCity.StartsWith(country + RegionCode.Separator), ct))
            doomed.Add(country);

        if (doomed.Count == 0) return;

        var removed = await bankHolidays.RemoveForRegionsAsync(doomed, ct);

        logger.LogInformation(
            "Removed {Count} bank holidays for {Regions}: no one is in them any more.",
            removed, string.Join(", ", doomed));
    }
}
