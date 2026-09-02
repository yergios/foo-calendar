namespace Api.Interfaces;

/// <summary>How many rows one retention sweep deleted, per table.</summary>
public readonly record struct RetentionSweepResult(int AuditEntries, int Notifications, int Absences)
{
    public int Total => AuditEntries + Notifications + Absences;
}

public interface IRetentionService
{
    /// <summary>Deletes everything that has outlived the configured retention periods, once.
    /// A no-op for any period left at RetentionOptions.Never.</summary>
    Task<RetentionSweepResult> SweepAsync(CancellationToken ct = default);
}
