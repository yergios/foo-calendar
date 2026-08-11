using Api.Data.Models;
using Api.Dtos;

namespace Api.Interfaces;

public interface IAbsenceService
{
    Task<AbsencesResponse> GetOverlappingAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AbsenceStatsResponse> GetStatsAsync(int personId, int year, CancellationToken ct = default);
    Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default);
    Task<AbsenceDto> CreateAsync(Person person, CreateAbsenceRequest request, CancellationToken ct = default);
    Task<AbsenceDto> UpdateAsync(Person person, int absenceId, CreateAbsenceRequest request, CancellationToken ct = default);
    Task DeleteAsync(int personId, int absenceId, CancellationToken ct = default);
}
