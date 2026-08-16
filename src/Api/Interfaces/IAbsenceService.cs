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
    Task<AbsenceStatsResponse> GetStatsAsync(int personId, int year, CancellationToken ct = default);
    Task<AllAbsenceStatsResponse> GetAllStatsAsync(int year, CancellationToken ct = default);
    Task<AbsenceDto> CreateAsync(Person person, CreateAbsenceRequest request, CancellationToken ct = default);
    Task<AbsenceDto> UpdateAsync(Person person, int absenceId, CreateAbsenceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Person person, int absenceId, CancellationToken ct = default);

    /// <summary>Every absence currently awaiting review, oldest first: whoever is approving
    /// works the queue in the order it built up. Always carries the requester's note, see
    /// PendingAbsencesResponse.</summary>
    Task<PendingAbsencesResponse> GetPendingAsync(CancellationToken ct = default);

    Task<AbsenceDto> ApproveAsync(Person reviewer, int absenceId, CancellationToken ct = default);

    /// <summary><paramref name="note"/> is the reviewer's own reason, optional, and never
    /// the requester's <see cref="Api.Data.Models.Absence.Note"/>.</summary>
    Task<AbsenceDto> RejectAsync(Person reviewer, int absenceId, string note, CancellationToken ct = default);
}
