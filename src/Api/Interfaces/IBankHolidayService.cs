using Api.Dtos;

namespace Api.Interfaces;

public interface IBankHolidayService
{
    Task<BankHolidaysResponse> GetForRegionsAsync(
        IReadOnlyCollection<string> regions,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);
    /// <summary>Syncs each country for the given year, continuing past individual failures. Returns the country codes that failed.</summary>
    Task<IReadOnlyList<string>> SyncCountriesAsync(
        IEnumerable<string> countryCodes, int year, CancellationToken ct = default);
    Task<BankHolidayDto> AddManualAsync(
        AddManualHolidayRequest request, string actorUsername, CancellationToken ct = default);
    Task<BankHolidayDto> UpdateManualAsync(
        int id, AddManualHolidayRequest request, string actorUsername, CancellationToken ct = default);
    Task DeleteManualAsync(int id, CancellationToken ct = default);
}
