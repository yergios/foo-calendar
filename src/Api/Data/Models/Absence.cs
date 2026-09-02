namespace Api.Data.Models;

// No Sick: an absence's type is visible to the whole team, and "this person is ill" is
// health data under GDPR Article 9, which carries a materially higher bar than the rest
// of what this app stores. Illness is recorded as Other, which says someone is away
// without saying why. See docs/data-protection.md.
public enum AbsenceType { Vacation, Other, Appointment }
public enum AbsenceStatus { Pending, Approved, Rejected }

public sealed class Absence
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null;

    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
    public required AbsenceType Type { get; set; }
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Approved;

    // Set when this absence is time-based rather than whole-day: always for
    // AbsenceType.Appointment, optionally for AbsenceType.Other. Plain wall-clock time,
    // never UTC and never converted per viewer: there's no zone attached, so there's
    // nothing to convert.
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public string Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The username of whoever performed the action. Currently always the absence's own
    // owner, since there's no admin-on-behalf-of flow for absences, but stored as its own
    // field rather than derived from Person so that doesn't have to stay true forever.
    public required string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }

    // Set together, once, by whoever approved or rejected this. Cleared back to null when
    // an edit sends an already-reviewed absence through a new approval flow, so these
    // always describe the current Status rather than some earlier review of it.
    public string ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>The reviewer's own reason, set only on a rejection. Distinct from
    /// <see cref="Note"/>, which is the requester's and never the reviewer's to write.</summary>
    public string ReviewNote { get; set; }
}
