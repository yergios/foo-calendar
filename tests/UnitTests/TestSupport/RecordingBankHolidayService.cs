using System.Threading.Channels;

using Api.Dtos;
using Api.Interfaces;
using Api.Services;

namespace UnitTests.TestSupport;

/// <summary>Answers GetForRegionsAsync from a flat, already-resolved list the test seeds
/// directly, mirroring just the region-matching behavior AbsenceService's stats
/// computation depends on: a queried region also pulls in its country's nationwide rows.
/// Recurring-holiday expansion is BankHolidayService's own job and is tested there, so
/// this fake doesn't reimplement it. Syncs are recorded rather than run, for the background
/// job's tests to wait on, and report no failures. Everything else throws: nothing here
/// needs to add or remove a holiday.</summary>
public sealed class RecordingBankHolidayService : IBankHolidayService
{
    public List<BankHolidayDto> Holidays { get; } = [];

    public Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var expanded = regions
            .SelectMany(RegionCode.SelfAndCountry)
            .ToHashSet(StringComparer.Ordinal);

        var matches = Holidays
            .Where(h => expanded.Contains(h.Region) && h.Date >= from && h.Date <= to)
            .ToList();

        return Task.FromResult(new BankHolidaysResponse(matches));
    }

    /// <summary>Every sync asked for, in order. A channel because the background job asks
    /// from a thread of its own, while the test waits for it.</summary>
    public Channel<(IReadOnlyList<string> Regions, int Year)> Syncs { get; } =
        Channel.CreateUnbounded<(IReadOnlyList<string> Regions, int Year)>();

    public Task<IReadOnlyList<string>> SyncRegionsAsync(
        IReadOnlyCollection<string> regions, int year, CancellationToken ct = default)
    {
        Syncs.Writer.TryWrite(([.. regions], year));
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<int> RemoveForRegionsAsync(IReadOnlyCollection<string> regions, CancellationToken ct = default) =>
        throw new NotSupportedException("Use BankHolidayService itself to test removal.");

    public Task<BankHolidayDto> AddManualAsync(
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Use BankHolidayService itself to test adding a holiday.");

    public Task<BankHolidayDto> UpdateManualAsync(
        int id,
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Use BankHolidayService itself to test updating a holiday.");

    public Task DeleteManualAsync(int id, CancellationToken ct = default) =>
        throw new NotSupportedException("Use BankHolidayService itself to test deleting a holiday.");
}
