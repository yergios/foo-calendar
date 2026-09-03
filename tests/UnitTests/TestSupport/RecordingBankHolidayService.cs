using Api.Dtos;
using Api.Interfaces;

namespace UnitTests.TestSupport;

/// <summary>Answers GetForRegionsAsync from a flat, already-resolved list the test seeds
/// directly, mirroring just the region-matching behavior AbsenceService's stats
/// computation depends on: a queried region also pulls in its country's nationwide rows.
/// Recurring-holiday expansion is BankHolidayService's own job and is tested there, so
/// this fake doesn't reimplement it. Everything else throws: nothing here needs to add,
/// sync, or remove a holiday.</summary>
public sealed class RecordingBankHolidayService : IBankHolidayService
{
    public List<BankHolidayDto> Holidays { get; } = [];

    public Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var expanded = regions
            .SelectMany(r => new[] { r, CountryOf(r) })
            .ToHashSet(StringComparer.Ordinal);

        var matches = Holidays
            .Where(h => expanded.Contains(h.Region) && h.Date >= from && h.Date <= to)
            .ToList();

        return Task.FromResult(new BankHolidaysResponse(matches));
    }

    private static string CountryOf(string region) =>
        region.Contains('-') ? region.Split('-')[0] : region;

    public Task<IReadOnlyList<string>> SyncRegionsAsync(
        IReadOnlyCollection<string> regions, int year, CancellationToken ct = default) =>
        throw new NotSupportedException("Use BankHolidayService itself to test syncing.");

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
