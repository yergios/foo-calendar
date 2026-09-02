using System.Linq.Expressions;

using Api.Data.Models;

namespace Api.Dtos;

public sealed record AbsenceDto(
    int Id,
    int PersonId,
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceType Type,
    AbsenceStatus Status,
    string Note,
    DateTime CreatedAt,
    TimeOnly? StartTime,
    TimeOnly? EndTime)
{
    /// <summary>The team can see that someone is off and what kind of absence it is, which
    /// is what the calendar is for. Why is nobody else's business: a note on a sick leave
    /// is health information, so <paramref name="includeNote"/> is false for everyone but
    /// the person it belongs to. Deliberately has no default, so the decision is made at
    /// every call site rather than inherited from one.</summary>
    public static AbsenceDto FromEntity(Absence absence, bool includeNote) => new(
        absence.Id,
        absence.PersonId,
        absence.StartDate,
        absence.EndDate,
        absence.Type,
        absence.Status,
        includeNote ? absence.Note : null,
        absence.CreatedAt,
        absence.StartTime,
        absence.EndTime);

    /// <summary>The same mapping as <see cref="FromEntity"/>, as an expression EF can
    /// translate, for reads that project straight out of the query instead of
    /// materializing entities first. <paramref name="viewerPersonId"/> plays the part of
    /// includeNote: only the viewer's own rows carry their note, decided in SQL so a note
    /// never leaves the database.</summary>
    public static Expression<Func<Absence, AbsenceDto>> Projection(int? viewerPersonId) => a => new AbsenceDto(
        a.Id,
        a.PersonId,
        a.StartDate,
        a.EndDate,
        a.Type,
        a.Status,
        a.PersonId == viewerPersonId ? a.Note : null,
        a.CreatedAt,
        a.StartTime,
        a.EndTime);
}
