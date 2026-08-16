namespace Api.Data.Models;

public enum NotificationType { AbsenceApproved, AbsenceRejected }

/// <summary>One thing a person is told about their own absence request, once it has been
/// reviewed. Denormalized from the absence it describes (type, dates, the reviewer's note)
/// rather than a foreign key to it, so it still reads correctly if that absence is later
/// edited or deleted, the same reasoning as WebhookAbsence.</summary>
public sealed class Notification
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null;

    public required NotificationType Type { get; set; }
    public required AbsenceType AbsenceType { get; set; }
    public required DateOnly StartDate { get; set; }
    public required DateOnly EndDate { get; set; }

    /// <summary>The reviewer's reason. Only ever set for a rejection.</summary>
    public string Note { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
