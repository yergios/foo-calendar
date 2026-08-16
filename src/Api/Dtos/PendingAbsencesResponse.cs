namespace Api.Dtos;

/// <summary>What a reviewer sees, so unlike AbsencesResponse this always carries the
/// requester's note: deciding whether to approve is the one situation this app asks
/// someone else to read it for.</summary>
public sealed record PendingAbsencesResponse(IReadOnlyList<AbsenceDto> Absences);
