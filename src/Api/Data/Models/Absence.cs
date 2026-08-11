namespace Api.Data.Models;

public enum AbsenceType { Vacation, Sick, Other }
public enum AbsenceStatus { Pending, Approved, Rejected }

public sealed class Absence
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null;

    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }
    public required AbsenceType Type { get; set; }
    public AbsenceStatus Status { get; set; } = AbsenceStatus.Approved; // hardcoded for now
    public string Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The username of whoever performed the action. Currently always the absence's own
    // owner, since there's no admin-on-behalf-of flow for absences, but stored as its own
    // field rather than derived from Person so that doesn't have to stay true forever.
    public required string CreatedBy { get; set; }
    public string UpdatedBy { get; set; }
}
