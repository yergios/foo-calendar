using Api.Dtos;
using Api.Services;

namespace Api.Interfaces;

public interface IPersonService
{
    Task<PeopleResponse> GetAllAsync(CancellationToken ct = default);

    /// <summary>Same base ordering as GetAllAsync, one page of it, narrowed by
    /// <paramref name="search"/> (display name, substring) and <paramref name="regions"/>
    /// (a home city matching any of them, or one of their subdivisions - same rule as the
    /// home page's region filter). <paramref name="sortBy"/> is one of the
    /// <see cref="PersonSortColumn"/> constants; a stats column pulls the filtered set into
    /// memory to sort by <paramref name="statsYear"/>'s numbers rather than paging at the
    /// database, since those aren't columns the database has (see
    /// AbsenceService.GetAllStatsAsync). For the admin people list, which - unlike the
    /// calendar - never needs the whole roster in memory at once outside that one case.
    /// </summary>
    Task<PeoplePageResponse> GetPageAsync(
        int page,
        int pageSize,
        string search,
        IReadOnlyList<string> regions,
        string sortBy,
        bool descending,
        int? statsYear,
        CancellationToken ct = default);
    Task<PersonDto> CreateAsync(CreatePersonRequest request, string actorUsername, CancellationToken ct = default);

    /// <summary>Creates a person per CSV row. Existing usernames are skipped rather than
    /// updated, so re-running the same file is safe.</summary>
    Task<ImportPeopleResponse> ImportAsync(string csv, string actorUsername, CancellationToken ct = default);
    Task DeleteAsync(int personId, CancellationToken ct = default);
    Task<PersonDto> UpdateAsync(
        int personId, UpdatePersonRequest request, string actorUsername, CancellationToken ct = default);
}
