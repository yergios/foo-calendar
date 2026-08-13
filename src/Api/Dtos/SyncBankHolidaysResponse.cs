namespace Api.Dtos;

/// <summary>Regions rather than countries, because that is what an admin picked and what
/// the calendar tracks. A failure is still a whole country's fetch failing underneath, so
/// every region of that country shows up in <see cref="FailedRegions"/> together.</summary>
public sealed record SyncBankHolidaysResponse(
    IReadOnlyList<string> SyncedRegions,
    IReadOnlyList<string> FailedRegions);
