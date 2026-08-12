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
}
