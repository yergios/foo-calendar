namespace Api.Dtos;

public sealed record BankHolidaysResponse(IReadOnlyList<BankHolidayDto> Holidays);
