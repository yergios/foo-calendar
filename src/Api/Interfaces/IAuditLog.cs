using Api.Dtos;
using Api.Services;

namespace Api.Interfaces;

public interface IAuditLog
{
    /// <summary>Records one action, attributing it to whoever is signed in. Call it after
    /// the change has been committed, so nothing is recorded for a change that then failed
    /// to save.</summary>
    Task RecordAsync(
        string action,
        string target,
        string details = null,
        CancellationToken ct = default);

    /// <summary>Newest first by default. <paramref name="search"/> matches <c>Actor</c> as a
    /// substring; <paramref name="action"/> matches <c>Action</c> exactly.
    /// <paramref name="from"/>/<paramref name="to"/> bound <c>OccurredAt</c> (inclusive
    /// lower, exclusive upper - the caller is responsible for the exact instants, so a
    /// calendar-day filter can account for the viewer's own timezone). <paramref
    /// name="sortBy"/> is one of the <see cref="AuditSortColumn"/> constants.</summary>
    Task<AuditEntriesResponse> ReadAsync(
        int limit,
        int offset,
        string search = null,
        string action = null,
        DateTime? from = null,
        DateTime? to = null,
        string sortBy = AuditSortColumn.OccurredAt,
        bool descending = true,
        CancellationToken ct = default);
}
