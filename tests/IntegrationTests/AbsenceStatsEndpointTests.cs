using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;
using Api.Endpoints;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

public sealed class AbsenceStatsEndpointTests
{
    private static readonly Uri MyStats = new("/api/people/me/stats?year=2026", UriKind.Relative);
    private static readonly Uri EveryonesStats = new("/api/admin/people/stats?year=2026", UriKind.Relative);

    // The dev identity's own profile with a three-weekday vacation in March 2026, and one
    // other person's with a single day.
    private static async Task<HttpClient> SignedInWithAbsencesAsync(TestApp app)
    {
        var client = app.CreateClient();

        using var profile = await client.PostAsJsonAsync(
            new Uri("/api/people", UriKind.Relative), new UpdatePersonRequest("Me", null));
        profile.EnsureSuccessStatusCode();

        using var absence = await client.PostAsJsonAsync(
            new Uri("/api/absences", UriKind.Relative),
            new CreateAbsenceRequest(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4), AbsenceType.Vacation, null));
        absence.EnsureSuccessStatusCode();

        app.Seed(db =>
        {
            var other = Rows.Person("other");
            db.People.Add(other);
            db.Absences.Add(Rows.Vacation(other, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 9)));
        });

        return client;
    }

    [Fact]
    [Trait("Spec", "ABS-11")]
    public async Task AMember_ReadsTheirOwnTotals()
    {
        using var app = new TestApp { Permissions = [] };
        using var client = await SignedInWithAbsencesAsync(app);

        var stats = await client.GetFromJsonAsync<AbsenceStatsResponse>(MyStats);

        stats.ShouldBe(new AbsenceStatsResponse(2026, VacationDays: 3, OtherDays: 0));
    }

    [Fact]
    [Trait("Spec", "ABS-11")]
    public async Task EveryonesTotals_WithoutReportsRead_AreForbidden()
    {
        using var app = new TestApp
        {
            Permissions = [.. Permissions.All.Where(p => p != Permissions.ReportsRead)],
        };
        using var client = await SignedInWithAbsencesAsync(app);

        using var response = await client.GetAsync(EveryonesStats);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Spec", "ABS-11")]
    public async Task EveryonesTotals_WithReportsRead_ListEveryPerson()
    {
        using var app = new TestApp { Permissions = [Permissions.ReportsRead] };
        using var client = await SignedInWithAbsencesAsync(app);

        var stats = (await client.GetFromJsonAsync<AllAbsenceStatsResponse>(EveryonesStats)).ShouldNotBeNull();

        var people = app.Query(db => db.People.ToDictionary(p => p.Id, p => p.Username));
        stats.Stats
            .Select(s => (Username: people[s.PersonId], s.VacationDays))
            .OrderBy(s => s.Username, StringComparer.Ordinal)
            .ShouldBe([("dev-user", 3), ("other", 1)]);
    }

    // 0 and 10000 are years DateOnly cannot hold. 9999 it can, but counting working days
    // steps past its last day, so it failed as soon as anyone had an absence ending there.
    [Theory]
    [Trait("Spec", "ABS-11")]
    [Trait("Spec", "ERR-1")]
    [InlineData("/api/people/me/stats", 0)]
    [InlineData("/api/people/me/stats", RequestYear.Min - 1)]
    [InlineData("/api/people/me/stats", RequestYear.Max + 1)]
    [InlineData("/api/people/me/stats", 9999)]
    [InlineData("/api/admin/people/stats", 0)]
    [InlineData("/api/admin/people/stats", -1)]
    [InlineData("/api/admin/people/stats", RequestYear.Max + 1)]
    [InlineData("/api/admin/people/stats", 10000)]
    [InlineData("/api/people/paged", 0)]
    [InlineData("/api/people/paged", 10000)]
    public async Task AYearOutOfRange_IsABadRequestNamingTheRange(string route, int year)
    {
        using var app = new TestApp();
        using var client = await SignedInWithAbsencesAsync(app);

        using var response = await client.GetAsync(
            new Uri($"{route}?year={year}&sortBy=vacation", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString()
            .ShouldBe($"'year' must be between {RequestYear.Min} and {RequestYear.Max}; got {year}.");
    }

    // Both ends, each with an absence running to the year's edge, which is where the
    // working-day count steps one day beyond it.
    [Theory]
    [Trait("Spec", "ABS-11")]
    [InlineData(RequestYear.Min)]
    [InlineData(RequestYear.Max)]
    public async Task TheYearsAtEitherEndOfTheRange_AreCounted(int year)
    {
        using var app = new TestApp();
        using var client = await SignedInWithAbsencesAsync(app);
        foreach (var (start, end) in new[]
        {
            (new DateOnly(year, 1, 1), new DateOnly(year, 1, 1)),
            (new DateOnly(year, 12, 31), new DateOnly(year, 12, 31)),
        })
        {
            using var absence = await client.PostAsJsonAsync(
                new Uri("/api/absences", UriKind.Relative),
                new CreateAbsenceRequest(start, end, AbsenceType.Vacation, null));
            absence.EnsureSuccessStatusCode();
        }

        var mine = await client.GetFromJsonAsync<AbsenceStatsResponse>(
            new Uri($"/api/people/me/stats?year={year}", UriKind.Relative));
        using var everyone = await client.GetAsync(
            new Uri($"/api/admin/people/stats?year={year}", UriKind.Relative));

        var weekdays = new[] { new DateOnly(year, 1, 1), new DateOnly(year, 12, 31) }
            .Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        mine.ShouldBe(new AbsenceStatsResponse(year, VacationDays: weekdays, OtherDays: 0));
        everyone.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
