using Api.Interfaces;

namespace UnitTests.TestSupport;

/// <summary>Records what PersonService asks of it, so the lifecycle tests can assert that
/// joining, moving, and leaving each trigger the right side of the sync/prune pair without
/// standing up a holiday source to watch it happen.</summary>
public sealed class RecordingTeamRegionService : ITeamRegionService
{
    public List<string> Synced { get; } = [];
    public List<string> Pruned { get; } = [];
    public IReadOnlyList<string> InUse { get; set; } = [];

    public Task<IReadOnlyList<string>> InUseAsync(CancellationToken ct = default) =>
        Task.FromResult(InUse);

    public Task EnsureSyncedAsync(string region, CancellationToken ct = default)
    {
        Synced.Add(region);
        return Task.CompletedTask;
    }

    public Task PruneIfUnusedAsync(string region, CancellationToken ct = default)
    {
        Pruned.Add(region);
        return Task.CompletedTask;
    }
}
