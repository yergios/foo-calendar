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

    private void StartCase()
    {
        Db.Absences.ExecuteDelete();
        Db.People.ExecuteDelete();
        Db.ChangeTracker.Clear();
        bankHolidays.Holidays.Clear();
    }

    private Person AddPerson(string username, string? homeCity = null)
    {
        var person = new Person
        {
            Username = username,
            DisplayName = username,
            HomeCity = homeCity,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username,
        };
        Db.People.Add(person);
        Db.SaveChanges();
        return person;
    }

    // Stored directly rather than created, so the existing absences can be anything,
    // including ones the rule would have refused. Returns their ids, in order.
    private List<int> Store(Person person, IEnumerable<Span> spans)
    {
        var stored = spans.Select(span => new Absence
        {
            PersonId = person.Id,
            StartDate = span.Start,
            EndDate = span.End,
            Type = span.Type,
            StartTime = span.TimeBased ? new TimeOnly(9, 0) : null,
            EndTime = span.TimeBased ? new TimeOnly(10, 0) : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = person.Username,
        }).ToList();
        Db.Absences.AddRange(stored);
        Db.SaveChanges();
        return [.. stored.Select(a => a.Id)];
    }

    // A whole-day absence shares no day with any other; time-based ones may share a day
    // with each other, but not with a whole-day one.
    private static bool Conflicts(Span requested, IEnumerable<Span> others)
    {
        var requestedDays = requested.Days().ToHashSet();
        return others
            .Where(e => !(e.TimeBased && requested.TimeBased))
            .Any(e => requestedDays.Overlaps(e.Days()));
    }

    private static CreateAbsenceRequest RequestFor(Span span) => new(
        span.Start, span.End, span.Type, null,
        span.TimeBased ? new TimeOnly(11, 0) : null,
        span.TimeBased ? new TimeOnly(12, 0) : null);

    public sealed record OverlapCase(List<Span> Existing, Span Requested);

    private static readonly DateOnly WindowStart = new(2026, 3, 1);
    private static readonly DateOnly WindowEnd = new(2026, 3, 21);

    // A three-week window, so most cases have something to collide with.
    [Fact]
    [Trait("Spec", "ABS-5")]
    public Task TheOverlapRule_OnCreate_AgreesWithIntersectingTheDaysCovered() => Property.CheckAsync(
        Cases,
        gen => new OverlapCase(
            gen.List(0, 4, g => GenerateSpan(g, WindowStart, WindowEnd)), GenerateSpan(gen, WindowStart, WindowEnd)),
        async c =>
        {
            StartCase();
            var person = AddPerson("jdoe");
            Store(person, c.Existing);

            if (Conflicts(c.Requested, c.Existing))
                await Should.ThrowAsync<ConflictException>(() => sut.CreateAsync(person, RequestFor(c.Requested)));
            else
                await Should.NotThrowAsync(() => sut.CreateAsync(person, RequestFor(c.Requested)));
        });

    public sealed record UpdateCase(List<Span> Existing, int Updated, Span Requested);

    // An update is checked against every other absence of theirs, never against the one it
    // replaces.
    [Fact]
    [Trait("Spec", "ABS-5")]
    public Task TheOverlapRule_OnUpdate_AgreesWithIntersectingTheDaysCovered() => Property.CheckAsync(
        Cases,
        gen =>
        {
            var existing = gen.List(1, 5, g => GenerateSpan(g, WindowStart, WindowEnd));
            return new UpdateCase(existing, gen.Between(0, existing.Count - 1), GenerateSpan(gen, WindowStart, WindowEnd));
        },
        async c =>
        {
            StartCase();
            var person = AddPerson("jdoe");
            var ids = Store(person, c.Existing);
            Db.ChangeTracker.Clear();

            var others = c.Existing.Where((_, i) => i != c.Updated);

            if (Conflicts(c.Requested, others))
                await Should.ThrowAsync<ConflictException>(() => sut.UpdateAsync(person, ids[c.Updated], RequestFor(c.Requested)));
            else
                await Should.NotThrowAsync(() => sut.UpdateAsync(person, ids[c.Updated], RequestFor(c.Requested)));
        });

    public sealed record Member(string? HomeCity, List<Span> Absences);

    public sealed record TotalsCase(int Year, List<Member> People, List<BankHolidayDto> Holidays);

    // Sibling subdivisions of one country, the country itself, another country and nowhere,
    // so that one person's holidays sit beside holidays that are not theirs.
    private static readonly string?[] HomeCities = ["ES-MD", "ES-CT", "ES", "FR-BRE", null];
    private static readonly string[] HolidayRegions = ["ES", "ES-MD", "ES-CT", "FR", "FR-BRE", "FR-IDF"];

    // A person's own holidays: their country's nationwide ones and their exact region's,
    // never a sibling region's. The country is the code before the hyphen.
    private static HashSet<DateOnly> OwnHolidays(string? homeCity, IEnumerable<BankHolidayDto> holidays) =>
        homeCity is null
            ? []
            : [.. holidays.Where(h => h.Region == homeCity || h.Region == homeCity.Split('-')[0]).Select(h => h.Date)];

    private static (int Vacation, int Other) CountDayByDay(int year, List<Span> absences, HashSet<DateOnly> ownHolidays)
    {
        int vacation = 0, other = 0;
        for (var day = new DateOnly(year, 1, 1); day.Year == year; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || ownHolidays.Contains(day)) continue;

            foreach (var absence in absences.Where(a => !a.TimeBased && a.Start <= day && day <= a.End))
            {
                if (absence.Type == AbsenceType.Vacation) vacation++;
                else other++;
            }
        }

        return (vacation, other);
    }

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
            // The days either side of each year boundary, sometimes, where clamping and
            // counting meet a holiday.
            DateOnly[] yearEdges = [new(year - 1, 12, 31), new(year, 1, 1), new(year, 12, 31), new(year + 1, 1, 1)];
            var holidays = gen.List(0, 25, g => new BankHolidayDto(
                0,
                g.Pick(HolidayRegions),
                g.Chance(0.15) ? g.Pick(yearEdges) : g.Date(from, to.AddDays(10)),
                "Holiday",
                HolidaySource.Manual,
                false));
            var people = gen.List(1, 5, g => new Member(g.Pick(HomeCities), g.List(0, 8, h => GenerateSpan(h, from, to))));
            return new TotalsCase(year, people, holidays);
        },
        async c =>
        {
            StartCase();
            var people = c.People.Select((member, i) => (Person: AddPerson($"person{i}", member.HomeCity), member)).ToList();
            foreach (var (person, member) in people)
                Store(person, member.Absences);
            bankHolidays.Holidays.AddRange(c.Holidays);

            var everyone = await sut.GetAllStatsAsync(c.Year);
            everyone.Stats.Count.ShouldBe(people.Count);

            foreach (var (person, member) in people)
            {
                var expected = CountDayByDay(c.Year, member.Absences, OwnHolidays(member.HomeCity, c.Holidays));

                var mine = await sut.GetStatsAsync(person, c.Year);
                (mine.VacationDays, mine.OtherDays).ShouldBe(expected, $"GetStatsAsync for {person.Username}");

                var theirs = everyone.Stats.Single(s => s.PersonId == person.Id);
                (theirs.VacationDays, theirs.OtherDays).ShouldBe(expected, $"GetAllStatsAsync for {person.Username}");
            }
        });
}
