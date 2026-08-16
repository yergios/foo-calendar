using Api.Data.Models;
using Api.Dtos;

namespace Api.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(
        Absence absence, NotificationType type, string note, CancellationToken ct = default);

    /// <summary>Newest first, capped: this is a feed of recent reviews, not a full history
    /// someone needs to page back through.</summary>
    Task<NotificationsResponse> GetForPersonAsync(int personId, CancellationToken ct = default);

    /// <summary>Marks every one of this person's unread notifications read. Per-notification
    /// read state would be more precise, but nobody has asked to dismiss one at a time out
    /// of a feed this short; opening it having read the rest is enough.</summary>
    Task MarkAllReadAsync(int personId, CancellationToken ct = default);
}
