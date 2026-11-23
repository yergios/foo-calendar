using System.Net;
using System.Net.Http.Json;

using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;

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

        var stats = await client.GetFromJsonAsync<AllAbsenceStatsResponse>(EveryonesStats);

        var people = app.Query(db => db.People.ToDictionary(p => p.Id, p => p.Username));
        stats.Stats
            .Select(s => (Username: people[s.PersonId], s.VacationDays))
            .OrderBy(s => s.Username, StringComparer.Ordinal)
            .ShouldBe([("dev-user", 3), ("other", 1)]);
    }
}
