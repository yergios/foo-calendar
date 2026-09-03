using Api.Data.Models;

namespace Api.Dtos;

public sealed record NotificationDto(
    int Id,
    NotificationType Type,
    AbsenceType AbsenceType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Note,
    DateTime CreatedAt,
    DateTime? ReadAt)
{
    public static NotificationDto FromEntity(Notification notification) => new(
        notification.Id,
        notification.Type,
        notification.AbsenceType,
        notification.StartDate,
        notification.EndDate,
        notification.Note,
        notification.CreatedAt,
        notification.ReadAt);
}

/// <summary>Newest first. UnreadCount is sent alongside the list rather than left for the
/// client to derive, so a page that only wants the badge doesn't have to fetch and count
/// every row itself.</summary>
public sealed record NotificationsResponse(
    IReadOnlyList<NotificationDto> Notifications,
    int UnreadCount,
    // Empty for anyone without AbsencesApprove, rather than omitted, so the client never
    // has to special-case "the field is missing" versus "nobody is waiting."
    IReadOnlyList<PendingApprovalDto> PendingApprovals);

/// <summary>One absence request waiting on this viewer's decision, oldest first - same
/// ordering as the approvals page itself. Not backed by a table of its own: unlike a
/// review notification, which is a one-time event a person is told about once, this is a
/// live view over whatever is still Pending right now, so it can never go stale the way a
/// stored "you have something to approve" record would if it outlived the request it
/// described.</summary>
public sealed record PendingApprovalDto(
    int AbsenceId,
    string PersonName,
    AbsenceType Type,
    DateOnly StartDate,
    DateOnly EndDate);
