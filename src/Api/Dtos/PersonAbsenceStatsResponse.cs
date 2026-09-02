namespace Api.Dtos;

public sealed record PersonAbsenceStatsResponse(
    int PersonId,
    int Year,
    int VacationDays,
    int OtherDays);
