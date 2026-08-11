namespace Api.Dtos;

public sealed record AllAbsenceStatsResponse(IReadOnlyList<PersonAbsenceStatsResponse> Stats);
