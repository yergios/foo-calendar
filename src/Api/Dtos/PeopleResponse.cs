namespace Api.Dtos;

/// <summary>The team, and display names for every region it occupies plus the countries
/// those regions sit in. The labels travel with the people rather than with /api/config
/// because the set follows who is on the team: config is cached until a redeploy, so a
/// label put there would go stale the moment somebody moved.</summary>
public sealed record PeopleResponse(
    IReadOnlyList<PersonDto> People,
    IReadOnlyDictionary<string, string> RegionLabels);
