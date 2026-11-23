using Api.Data.Models;
using Api.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

/// <summary>Recurring holidays as a query sees them, against a model that walks the years.
/// </summary>
public sealed class BankHolidayServicePropertyTests : SqliteTestBase
{
    private readonly BankHolidayService sut;

    public BankHolidayServicePropertyTests()
    {
        sut = new BankHolidayService(
            Db, new DisabledHolidaySource(), new RecordingAuditLog(), NullLogger<BankHolidayService>.Instance);
    }

    public sealed record RecurringCase(DateOnly Stored, DateOnly From, DateOnly To);

    [Fact]
    [Trait("Spec", "HOL-8")]
    public Task ARecurringHoliday_AppearsOnceInEveryYearTheQueryTouches() => Property.CheckAsync(
        500,
        gen =>
        {
            // Stored in a year of its own, and 29 February often, which only some years have.
            var year = gen.Between(2000, 2040);
            var stored = DateTime.IsLeapYear(year) && gen.Chance(0.3)
                ? new DateOnly(year, 2, 29)
                : gen.Date(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
            var from = gen.Date(new DateOnly(1990, 1, 1), new DateOnly(2060, 12, 31));
            return new RecurringCase(stored, from, from.AddDays(gen.Between(0, 4 * 366)));
        },
        async c =>
        {
            Db.BankHolidays.ExecuteDelete();
            Db.BankHolidays.Add(new BankHoliday
            {
                Region = "CH",
                Date = c.Stored,
                Name = "Every year",
                Source = HolidaySource.Manual,
                IsRecurringAnnually = true,
                CreatedBy = "admin",
            });
            Db.SaveChanges();

            var expected = new List<DateOnly>();
            for (var year = c.From.Year; year <= c.To.Year; year++)
            {
                if (c.Stored is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year)) continue;
                var occurrence = new DateOnly(year, c.Stored.Month, c.Stored.Day);
                if (c.From <= occurrence && occurrence <= c.To) expected.Add(occurrence);
            }

            var result = await sut.GetForRegionsAsync(["CH-ZH"], c.From, c.To);

            result.Holidays.Select(h => h.Date).ShouldBe(expected);
        });
}
