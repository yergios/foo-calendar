namespace Api.Dtos;

/// <summary>The team, and display names for every region it occupies plus the countries
/// those regions sit in. The labels travel with the people rather than with /api/config
/// because the set follows who is on the team: config is cached until a redeploy, so a
/// label put there would go stale the moment somebody moved.</summary>
public sealed record PeopleResponse(
    IReadOnlyList<PersonDto> People,
    IReadOnlyDictionary<string, string> RegionLabels);

/// <summary><paramref name="Total"/> is the whole team, not this page, so the admin page
/// can show a page count without fetching everyone just to learn its size - same shape as
/// AuditEntriesResponse.</summary>
public sealed record PeoplePageResponse(IReadOnlyList<PersonDto> People, int Total);
