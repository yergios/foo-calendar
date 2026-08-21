using Api.Dtos;

namespace Api.Interfaces;

public interface IPersonService
{
    Task<PeopleResponse> GetAllAsync(CancellationToken ct = default);

    /// <summary>Same ordering as GetAllAsync, one page of it. For the admin people list,
    /// which - unlike the calendar - never needs the whole roster in memory at once.</summary>
    Task<PeoplePageResponse> GetPageAsync(int page, int pageSize, CancellationToken ct = default);
    Task<PersonDto> CreateAsync(CreatePersonRequest request, string actorUsername, CancellationToken ct = default);

    /// <summary>Creates a person per CSV row. Existing usernames are skipped rather than
    /// updated, so re-running the same file is safe.</summary>
    Task<ImportPeopleResponse> ImportAsync(string csv, string actorUsername, CancellationToken ct = default);
    Task DeleteAsync(int personId, CancellationToken ct = default);
    Task<PersonDto> UpdateAsync(
        int personId, UpdatePersonRequest request, string actorUsername, CancellationToken ct = default);
}
