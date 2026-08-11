using Api.Configuration;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public sealed record AppConfigResponse(List<CountryRegions> Regions, List<string> ColorPalette);

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", GetConfig).RequireAuthorization();
    }

    private static IResult GetConfig(
        IOptions<RegionCatalogOptions> regions,
        IOptions<PersonColorPaletteOptions> palette) =>
        Results.Ok(new AppConfigResponse(regions.Value, palette.Value));
}
