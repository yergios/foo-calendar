using Api.Dtos;

namespace Api.Interfaces;

public interface IBankHolidayService
{
    Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    /// <summary>Syncs the given regions for one year, continuing past individual failures.
    /// Each region brings its own country's nationwide holidays with it. Returns the
    /// regions that failed, which is always every region of a country whose fetch failed:
    /// sources deal in whole countries, so that is the granularity a failure has.</summary>
    Task<IReadOnlyList<string>> SyncRegionsAsync(
        IReadOnlyCollection<string> regions, int year, CancellationToken ct = default);

    /// <summary>Deletes every holiday stored against these exact region codes, manually
    /// added ones included, and returns how many rows went. Exact, not by country: removing
    /// "ES" must leave "ES-AS" alone.</summary>
    Task<int> RemoveForRegionsAsync(
        IReadOnlyCollection<string> regions, CancellationToken ct = default);

    /// <summary>Adds a holiday by hand. <paramref name="trackedRegions"/> is the set the
    /// team currently occupies; anything outside it, or outside those regions' countries,
    /// is rejected rather than stored where nobody would ever see it.</summary>
    Task<BankHolidayDto> AddManualAsync(
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default);

    Task<BankHolidayDto> UpdateManualAsync(
        int id,
        AddManualHolidayRequest request,
        string actorUsername,
        IReadOnlyCollection<string> trackedRegions,
        CancellationToken ct = default);

    Task DeleteManualAsync(int id, CancellationToken ct = default);
}
