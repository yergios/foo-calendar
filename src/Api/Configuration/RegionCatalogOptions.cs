namespace Api.Configuration;

public sealed record RegionOption(string Code, string Label);
public sealed record CountryRegions(string Code, string Name, List<RegionOption> Regions);

/// <summary>Display name overrides for country and region codes. Which regions the
/// calendar actually tracks is derived from where people say they are plus
/// <see cref="TeamOptions.HomeRegion"/>, so this no longer decides what is selectable.
/// It exists for the deployments that want "Madrid" where the bundled CLDR table says
/// "Madrid Autonomous Community", and to name whatever CLDR has no entry for. Leaving it
/// empty is fine and normal.</summary>
public sealed class RegionCatalogOptions : List<CountryRegions>
{
    public const string SectionName = "RegionCatalog";
}
