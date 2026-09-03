using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;

namespace UnitTests.TestSupport;

/// <summary>Keeps what a service asked to notify a person about, so a test can assert on it
/// without a database round trip. Reading a feed back and marking it read are
/// NotificationService's own job and are tested there.</summary>
public sealed class RecordingNotificationService : INotificationService
{
    public sealed record Entry(int PersonId, NotificationType Type, AbsenceType AbsenceType, string Note);

    public List<Entry> Notified { get; } = [];

    public Task NotifyAsync(
        Absence absence, NotificationType type, string note, CancellationToken ct = default)
    {
        Notified.Add(new Entry(absence.PersonId, type, absence.Type, note));
        return Task.CompletedTask;
    }

    public Task<NotificationsResponse> GetForPersonAsync(
        int personId, bool includePendingApprovals, CancellationToken ct = default) =>
        throw new NotSupportedException("Use NotificationService itself to test reading a feed back.");

    public Task MarkAllReadAsync(int personId, CancellationToken ct = default) =>
        throw new NotSupportedException("Use NotificationService itself to test marking a feed read.");
}
