using Api.Data.Models;
using Api.Dtos;

namespace Api.Interfaces;

public interface IAbsenceService
{
    /// <summary><paramref name="viewerPersonId"/> is whose notes come back with the
    /// absences: their own and nobody else's. Null for someone with no profile yet, who
    /// can read the calendar but owns nothing on it.</summary>
    Task<AbsencesResponse> GetOverlappingAsync(
        DateOnly from, DateOnly to, int? viewerPersonId, CancellationToken ct = default);
    Task<AbsenceStatsResponse> GetStatsAsync(Person person, int year, CancellationToken ct = default);
    Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default);
    Task<AbsenceDto> CreateAsync(Person person, CreateAbsenceRequest request, CancellationToken ct = default);
    Task<AbsenceDto> UpdateAsync(Person person, int absenceId, CreateAbsenceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Person person, int absenceId, CancellationToken ct = default);
}
