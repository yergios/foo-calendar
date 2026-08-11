using Api.Data.Models;

namespace Api.Dtos;

public sealed record BankHolidayDto(
    int Id,
    string Region,
    DateOnly Date,
    string Name,
    HolidaySource Source,
    bool IsRecurringAnnually)
{
    public static BankHolidayDto FromEntity(BankHoliday holiday) => new(
        holiday.Id,
        holiday.Region,
        holiday.Date,
        holiday.Name,
        holiday.Source,
        holiday.IsRecurringAnnually);
}
