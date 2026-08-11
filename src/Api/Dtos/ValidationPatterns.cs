namespace Api.Dtos;

public static class ValidationPatterns
{
    /// <summary>ISO 3166-2 region/country code, e.g. "ES" or "ES-MD".</summary>
    public const string RegionCode = "^[A-Z]{2}(-[A-Z0-9]{1,3})?$";

    /// <summary>6-digit hex color, e.g. "#EC4899".</summary>
    public const string HexColor = "^#[0-9A-Fa-f]{6}$";
}
