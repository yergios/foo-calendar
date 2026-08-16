using Api.Data;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed class NotificationService(AppDbContext db) : INotificationService
{
    private const int FeedSize = 50;

    public async Task NotifyAsync(
        Absence absence, NotificationType type, string note, CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification
        {
            PersonId = absence.PersonId,
            Type = type,
            AbsenceType = absence.Type,
            StartDate = absence.StartDate,
            EndDate = absence.EndDate,
            Note = note,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationsResponse> GetForPersonAsync(int personId, CancellationToken ct = default)
    {
        // Ordered by Id, same reasoning as AuditLog: two rows written in the same
        // millisecond still have to come back in the order they happened.
        var notifications = await db.Notifications
            .Where(n => n.PersonId == personId)
            .OrderByDescending(n => n.Id)
            .Take(FeedSize)
            .ToListAsync(ct);

        var unreadCount = await db.Notifications
            .CountAsync(n => n.PersonId == personId && n.ReadAt == null, ct);

        return new([.. notifications.Select(NotificationDto.FromEntity)], unreadCount);
    }

    public async Task MarkAllReadAsync(int personId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // A tracked update, not ExecuteUpdateAsync: the latter writes straight to the
        // database without touching the change tracker, so a notification already loaded
        // on this same context (as GetForPersonAsync's caller may have just done) would
        // keep reading back its stale, unread in-memory value.
        var unread = await db.Notifications
            .Where(n => n.PersonId == personId && n.ReadAt == null)
            .ToListAsync(ct);

        foreach (var notification in unread)
            notification.ReadAt = now;

        await db.SaveChangesAsync(ct);
    }
}
