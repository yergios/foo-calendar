namespace Api.Interfaces;

/// <summary>Which regions the calendar tracks, and keeping the stored holidays in step
/// with that set as people come and go. The set is derived, not configured: it is
/// wherever people say they are, plus the team's home region.</summary>
public interface ITeamRegionService
{
    /// <summary>Every region the team currently occupies, plus the home region, which is
    /// in the set whether or not anyone has picked it.</summary>
    Task<IReadOnlyList<string>> InUseAsync(CancellationToken ct = default);

    /// <summary>Pulls in a region's holidays right after someone moves into it. Runs
    /// inline so the calendar is right by the time the profile page reloads, and is
    /// best-effort so a source being down can't fail the save.</summary>
    Task EnsureSyncedAsync(string? region, CancellationToken ct = default);

    /// <summary>Drops a region's holidays once the last person in it is gone. The home
    /// region is never dropped. Called after the person is already removed or moved, so
    /// the "is anyone left" question answers itself from the database.</summary>
    Task PruneIfUnusedAsync(string? region, CancellationToken ct = default);
}
