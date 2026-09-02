namespace Api.Dtos;

public sealed record AbsenceStatsResponse(
    int Year,
    int VacationDays,
    int OtherDays);
