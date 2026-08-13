using System.Collections.Concurrent;
using System.Net.Http.Json;

using Api.Data.Models;
using Api.Interfaces;

namespace Api.Services;

/// <summary>Public holidays from the Nager.Date API. Needs outbound internet access, or a
/// proxy: HttpClient picks up the standard HTTP_PROXY/HTTPS_PROXY/NO_PROXY environment
/// variables on its own, so no configuration here.</summary>
public sealed class NagerHolidaySource(HttpClient http) : IHolidaySource
{
    private sealed record NagerHoliday(
        DateOnly Date,
        string LocalName,
        string Name,
        string CountryCode,
        bool Global,
        string[] Counties,
        string[] Types);

    private sealed record NagerCountry(string CountryCode);

    // Which countries and subdivisions exist barely changes, and these back a picker that
    // is opened far more often than holidays are synced, so they are fetched once per
    // process. Static because AddHttpClient registers this type per request. Only
    // successful results are stored, so a transient failure isn't cached; the flip side is
    // that a country Nager adds later needs a restart to show up.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> Cache =
        new(StringComparer.Ordinal);

    // The country list shares the cache under a key no country code can take.
    private const string CountriesKey = "";

    public HolidaySource Kind => HolidaySource.Nager;

    public async Task<IReadOnlyList<SourceHoliday>> GetAsync(
        string countryCode, int year, CancellationToken ct = default)
    {
        var holidays = await http.GetFromJsonAsync<List<NagerHoliday>>(
            $"PublicHolidays/{year}/{countryCode}", ct);

        if (holidays is null) return [];

        // Global (nationwide) entries belong to the country code itself; the rest apply
        // to each county they list. Everything downstream sees plain region codes.
        return
        [
            .. holidays.SelectMany(
                h => h.Global ? [countryCode] : h.Counties ?? [],
                (h, region) => new SourceHoliday(region, h.Date, h.Name))
        ];
    }

    public async Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default)
    {
        if (Cache.TryGetValue(CountriesKey, out var cached)) return cached;

        var countries = await http.GetFromJsonAsync<List<NagerCountry>>("AvailableCountries", ct) ?? [];
        return Cache[CountriesKey] = [.. countries.Select(c => c.CountryCode).Order(StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<string>> GetRegionsAsync(
        string countryCode, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(countryCode, out var cached)) return cached;

        // Nager has no endpoint for subdivisions. The codes only ever appear inside a
        // year's holidays, attached to the non-nationwide ones, so a year has to be
        // fetched to find out which subdivisions a country even has.
        var holidays = await http.GetFromJsonAsync<List<NagerHoliday>>(
            $"PublicHolidays/{DateTime.UtcNow.Year}/{countryCode}", ct) ?? [];

        IReadOnlyList<string> regions =
        [
            .. holidays
                .SelectMany(h => h.Counties ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];

        return Cache[countryCode] = regions;
    }
}
