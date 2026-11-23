using Api.Data.Models;
using Api.Dtos;
using Api.Services;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

/// <summary>AbsenceService's overlap rule and totals, against models that work day by day.
/// </summary>
public sealed class AbsenceServicePropertyTests : SqliteTestBase
{
    private const int Cases = 300;

    private readonly RecordingBankHolidayService bankHolidays = new();
    private readonly AbsenceService sut;

    public AbsenceServicePropertyTests()
    {
        sut = new AbsenceService(Db, bankHolidays, new RecordingAuditLog());
    }

    public sealed record Span(DateOnly Start, DateOnly End, AbsenceType Type, bool TimeBased)
    {
        public IEnumerable<DateOnly> Days() =>
            Enumerable.Range(Start.DayNumber, End.DayNumber - Start.DayNumber + 1).Select(DateOnly.FromDayNumber);
    }

    private static Span GenerateSpan(Gen gen, DateOnly from, DateOnly to)
    {
        var start = gen.Date(from, to);
        if (gen.Chance(0.3))
            return new Span(start, start, gen.Pick([AbsenceType.Appointment, AbsenceType.Other]), TimeBased: true);

        return new Span(
            start, start.AddDays(gen.Between(0, 9)), gen.Pick([AbsenceType.Vacation, AbsenceType.Other]), TimeBased: false);
    }

    private Person StartCase(string homeCity = null)
    {
        Db.Absences.ExecuteDelete();
        Db.People.ExecuteDelete();
        Db.ChangeTracker.Clear();
        bankHolidays.Holidays.Clear();

        var person = new Person
        {
            Username = "jdoe",
            DisplayName = "Jane Doe",
            HomeCity = homeCity,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "jdoe",
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    // Stored directly rather than created, so the existing absences can be anything,
    // including ones the rule would have refused.
    private void Store(Person person, IEnumerable<Span> spans)
    {
        foreach (var span in spans)
        {
            Db.Absences.Add(new Absence
            {
                PersonId = person.Id,
                StartDate = span.Start,
                EndDate = span.End,
                Type = span.Type,
                StartTime = span.TimeBased ? new TimeOnly(9, 0) : null,
                EndTime = span.TimeBased ? new TimeOnly(10, 0) : null,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = person.Username,
            });
        }
        Db.SaveChanges();
    }

    public sealed record OverlapCase(List<Span> Existing, Span Requested);

    // A three-week window, so most cases have something to collide with.
    [Fact]
    [Trait("Spec", "ABS-5")]
    public Task TheOverlapRule_AgreesWithIntersectingTheDaysCovered() => Property.CheckAsync(
        Cases,
        gen =>
        {
            DateOnly from = new(2026, 3, 1), to = new(2026, 3, 21);
            return new OverlapCase(gen.List(0, 4, g => GenerateSpan(g, from, to)), GenerateSpan(gen, from, to));
        },
        async c =>
        {
            var person = StartCase();
            Store(person, c.Existing);

            // A whole-day absence shares no day with any other; time-based ones may share a
            // day with each other, but not with a whole-day one.
            var requestedDays = c.Requested.Days().ToHashSet();
            var conflicts = c.Existing
                .Where(e => !(e.TimeBased && c.Requested.TimeBased))
                .Any(e => requestedDays.Overlaps(e.Days()));

            var request = new CreateAbsenceRequest(
                c.Requested.Start, c.Requested.End, c.Requested.Type, null,
                c.Requested.TimeBased ? new TimeOnly(11, 0) : null,
                c.Requested.TimeBased ? new TimeOnly(12, 0) : null);

            if (conflicts)
                await Should.ThrowAsync<InvalidOperationException>(() => sut.CreateAsync(person, request));
            else
                await Should.NotThrowAsync(() => sut.CreateAsync(person, request));
        });

    public sealed record TotalsCase(int Year, string HomeCity, List<Span> Absences, List<BankHolidayDto> Holidays);

    private static readonly string[] HolidayRegions = ["ES", "ES-MD", "ES-CT", "FR"];

    [Fact]
    [Trait("Spec", "ABS-10")]
    [Trait("Spec", "HOL-9")]
    public Task WorkingDayTotals_AgreeWithCountingDayByDay() => Property.CheckAsync(
        Cases,
        gen =>
        {
            var year = gen.Between(2024, 2028);
            // Reaching a little into the years either side, to be clamped.
            DateOnly from = new(year - 1, 12, 20), to = new(year, 12, 31);
            var holidays = gen.List(0, 25, g => new BankHolidayDto(
                0, g.Pick(HolidayRegions), g.Date(from, to.AddDays(10)), "Holiday", HolidaySource.Manual, false));
            return new TotalsCase(
                year, gen.Pick<string>(["ES-MD", "ES", null]), gen.List(0, 8, g => GenerateSpan(g, from, to)), holidays);
        },
        async c =>
        {
            var person = StartCase(c.HomeCity);
            Store(person, c.Absences);
            bankHolidays.Holidays.AddRange(c.Holidays);

            // The person's own holidays: their country's nationwide ones and their exact
            // region's, never a sibling region's.
            var own = c.HomeCity is null
                ? []
                : c.Holidays
                    .Where(h => h.Region == c.HomeCity || h.Region == RegionCode.CountryOf(c.HomeCity))
                    .Select(h => h.Date)
                    .ToHashSet();

            int vacation = 0, other = 0;
            for (var day = new DateOnly(c.Year, 1, 1); day.Year == c.Year; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || own.Contains(day)) continue;

                foreach (var absence in c.Absences.Where(a => !a.TimeBased && a.Start <= day && day <= a.End))
                {
                    if (absence.Type == AbsenceType.Vacation) vacation++;
                    else other++;
                }
            }

            var mine = await sut.GetStatsAsync(person, c.Year);
            (mine.VacationDays, mine.OtherDays).ShouldBe((vacation, other), "GetStatsAsync");

            var everyone = await sut.GetAllStatsAsync(c.Year);
            var theirs = everyone.Stats.ShouldHaveSingleItem();
            (theirs.VacationDays, theirs.OtherDays).ShouldBe((vacation, other), "GetAllStatsAsync");
        });
}
