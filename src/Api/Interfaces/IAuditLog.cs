using Api.Dtos;

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

    /// <summary>Newest first.</summary>
    Task<AuditEntriesResponse> ReadAsync(int limit, int offset, CancellationToken ct = default);
}
