using System.Text.RegularExpressions;

using Api.Dtos;
using Api.Interfaces;
using Api.Services;

namespace Api.Endpoints;

public sealed record RegionOption(string Code, string Label);
public sealed record AvailableRegionsResponse(List<RegionOption> Regions);

/// <summary>What someone could pick on their profile, which is everything the holiday
/// source knows about rather than anything this team has chosen. Kept apart from
/// /api/config for that reason: config describes the deployment, this describes the
/// outside world, and it is fetched lazily because most of it is never looked at.
/// </summary>
public static class RegionEndpoints
{
    public static void MapRegionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/regions").RequireAuthorization().WithTags("Regions");

        group.MapGet("/countries", GetCountries);
        group.MapGet("/{countryCode}", GetRegions);
    }

    private static async Task<IResult> GetCountries(
        IHolidaySource source,
        RegionLabels labels,
        CancellationToken ct)
    {
        var countries = await source.GetCountriesAsync(ct);
        return Results.Ok(new AvailableRegionsResponse(Label(countries, labels)));
    }

    private static async Task<IResult> GetRegions(
        string countryCode,
        IHolidaySource source,
        RegionLabels labels,
        CancellationToken ct)
    {
        // Checked rather than trusted: this ends up interpolated into the upstream URL a
        // source builds, so it has to be a country code and nothing else.
        if (!Regex.IsMatch(countryCode, ValidationPatterns.RegionCode))
            throw new ArgumentException($"'{countryCode}' is not an ISO 3166 country code.");

        var regions = await source.GetRegionsAsync(countryCode, ct);
        return Results.Ok(new AvailableRegionsResponse(Label(regions, labels)));
    }

    // Sorted by what people read, not by the code behind it, so "Zürich" lands under Z.
    private static List<RegionOption> Label(IReadOnlyList<string> codes, RegionLabels labels) =>
        [.. codes
            .Select(code => new RegionOption(code, labels.For(code)))
            .OrderBy(r => r.Label, StringComparer.InvariantCulture)];
}
