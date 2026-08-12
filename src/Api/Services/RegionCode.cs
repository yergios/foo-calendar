namespace Api.Services;

/// <summary>ISO 3166-2 region codes: "ES" is a country, "ES-MD" a subdivision of it. This
/// is the one place that knows how the two relate, so the derivations can't drift apart.
/// </summary>
public static class RegionCode
{
    public const string Separator = "-";

    public static string CountryOf(string region) =>
        region.Contains(Separator, StringComparison.Ordinal) ? region.Split(Separator)[0] : region;

    /// <summary>True for the country's own code and for any of its subdivisions.</summary>
    public static bool BelongsTo(string region, string countryCode) =>
        region == countryCode || region.StartsWith(countryCode + Separator, StringComparison.Ordinal);
}
