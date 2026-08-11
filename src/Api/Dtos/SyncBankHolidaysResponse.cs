namespace Api.Dtos;

public sealed record SyncBankHolidaysResponse(
    IReadOnlyList<string> SyncedCountries,
    IReadOnlyList<string> FailedCountries);
