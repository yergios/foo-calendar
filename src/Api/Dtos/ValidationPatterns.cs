namespace Api.Dtos;

public static class ValidationPatterns
{
    /// <summary>ISO 3166-2 region/country code, e.g. "ES" or "ES-MD".</summary>
    public const string RegionCode = "^[A-Z]{2}(-[A-Z0-9]{1,3})?$";
}
