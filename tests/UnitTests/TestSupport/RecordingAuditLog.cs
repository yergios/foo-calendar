using Api.Dtos;
using Api.Interfaces;
using Api.Services;

namespace UnitTests.TestSupport;

/// <summary>Keeps what a service asked to be logged, so a test can assert on the entry
/// without a request, a signed-in user, or a second trip to the database. Reading back is
/// AuditLog's own job and is tested there.</summary>
public sealed class RecordingAuditLog : IAuditLog
{
    public sealed record Entry(string Action, string Target, string Details);

    public List<Entry> Entries { get; } = [];

    public Task RecordAsync(
        string action, string target, string details = null, CancellationToken ct = default)
    {
        Entries.Add(new Entry(action, target, details));
        return Task.CompletedTask;
    }

    public Task<AuditEntriesResponse> ReadAsync(
        int limit,
        int offset,
        string search = null,
        string action = null,
        DateTime? from = null,
        DateTime? to = null,
        string sortBy = AuditSortColumn.OccurredAt,
        bool descending = true,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Use AuditLog itself to test reading the log back.");
}
