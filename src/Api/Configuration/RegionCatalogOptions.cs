namespace Api.Configuration;

public sealed record RegionOption(string Code, string Label);
public sealed record CountryRegions(string Code, string Name, List<RegionOption> Regions);

public sealed class RegionCatalogOptions : List<CountryRegions>
{
    public const string SectionName = "RegionCatalog";
}
