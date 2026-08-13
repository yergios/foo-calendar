using System.Text.Json;

using Api.Configuration;

using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>Turns an ISO 3166-1 or 3166-2 code into something worth showing a human.
/// Three tiers, most specific first: the RegionCatalog config, then the bundled CLDR name
/// table, then the code itself. The last tier is why an unknown region degrades to
/// "SH-TA" in a dropdown instead of to a blank entry.</summary>
public sealed class RegionLabels
{
    // Sits next to the binary rather than inside it, see Api.csproj, so a deployment can
    // correct or translate a name without a rebuild.
    private static readonly string DataPath =
        Path.Combine(AppContext.BaseDirectory, "Data", "regions.json");

    private readonly Dictionary<string, string> names;
    private readonly Dictionary<string, string> overrides;

    public RegionLabels(IOptions<RegionCatalogOptions> catalog, ILogger<RegionLabels> logger)
    {
        overrides = catalog.Value
            .SelectMany(country => country.Regions.Append(new RegionOption(country.Code, country.Name)))
            .GroupBy(r => r.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);

        try
        {
            // Read once at startup: it is static reference data, and ~5,700 entries cost
            // far less to hold than to re-read on every dropdown.
            using var stream = File.OpenRead(DataPath);
            names = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch (Exception ex)
        {
            // Labels are cosmetic. A missing table leaves raw codes in the UI, which is
            // worse than it should be but not worth refusing to serve over.
            logger.LogWarning(ex,
                "Region name table could not be read from {Path}; falling back to raw codes.", DataPath);
            names = [];
        }
    }

    public string For(string code)
    {
        if (overrides.TryGetValue(code, out var label)) return label;

        return names.TryGetValue(code, out var name) ? name : code;
    }
}
