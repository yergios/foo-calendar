using Api.Dtos;

namespace Api.Interfaces;

public interface IPersonService
{
    Task<PeopleResponse> GetAllAsync(CancellationToken ct = default);
    Task<ColorsInUseResponse> GetColorsInUseAsync(CancellationToken ct = default);
    Task<PersonDto> CreateAsync(CreatePersonRequest request, string actorUsername, CancellationToken ct = default);

    /// <summary>Creates a person per CSV row. Existing usernames are skipped rather than
    /// updated, so re-running the same file is safe.</summary>
    Task<ImportPeopleResponse> ImportAsync(string csv, string actorUsername, CancellationToken ct = default);
    Task DeleteAsync(int personId, CancellationToken ct = default);
    Task<PersonDto> UpdateAsync(
        int personId, UpdatePersonRequest request, string actorUsername, CancellationToken ct = default);
}
