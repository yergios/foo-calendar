namespace Api.Data.Models;

// No Sick: an absence's type is visible to the whole team, and "this person is ill" is
// health data under GDPR Article 9, which carries a materially higher bar than the rest
// of what this app stores. Illness is recorded as Other, which says someone is away
// without saying why. See docs/data-protection.md.
public enum AbsenceType { Vacation, Other, Appointment }

public sealed class Absence
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    // Loaded only when a query includes it; nothing in the app does, so it is null on
    // every entity the app reads.
    public Person? Person { get; set; }

    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
    public required AbsenceType Type { get; set; }

    // Set when this absence is time-based rather than whole-day: always for
    // AbsenceType.Appointment, optionally for AbsenceType.Other. Plain wall-clock time,
    // never UTC and never converted per viewer: there's no zone attached, so there's
    // nothing to convert.
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The username of whoever performed the action. Currently always the absence's own
    // owner, since there's no admin-on-behalf-of flow for absences, but stored as its own
    // field rather than derived from Person so that doesn't have to stay true forever.
    public required string CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
