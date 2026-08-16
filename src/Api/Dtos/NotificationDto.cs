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
public sealed record NotificationsResponse(IReadOnlyList<NotificationDto> Notifications, int UnreadCount);
