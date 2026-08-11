using Api.Dtos;

namespace Api.Interfaces;

public interface IPersonService
{
    Task<PeopleResponse> GetAllAsync(CancellationToken ct = default);
    Task<ColorsInUseResponse> GetColorsInUseAsync(CancellationToken ct = default);
    Task<PersonDto> CreateAsync(CreatePersonRequest request, string actorUsername, CancellationToken ct = default);
    Task DeleteAsync(int personId, CancellationToken ct = default);
    Task<PersonDto> UpdateAsync(
        int personId, UpdatePersonRequest request, string actorUsername, CancellationToken ct = default);
}
