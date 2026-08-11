using Api.Data.Models;

namespace Api.Dtos;

public sealed record AbsenceDto(
    int Id,
    int PersonId,
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceType Type,
    string Note,
    DateTime CreatedAt)
{
    public static AbsenceDto FromEntity(Absence absence) => new(
        absence.Id,
        absence.PersonId,
        absence.StartDate,
        absence.EndDate,
        absence.Type,
        absence.Note,
        absence.CreatedAt);
}
