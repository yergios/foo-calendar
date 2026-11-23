using System.Net;
using System.Net.Http.Json;

using Api.Configuration;
using Api.Dtos;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

public sealed class PeopleEndpointTests
{
    private static readonly Uri People = new("/api/people", UriKind.Relative);

    private static Uri Url(string path) => new(path, UriKind.Relative);

    // The first sign-in, before anyone has set up a profile: the calendar is all there.
    [Theory]
    [Trait("Spec", "PPL-1")]
    [InlineData("/api/absences?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/bank-holidays/catalog?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/people")]
    public async Task AMemberWithoutAProfile_ReadsTheCalendar(string path)
    {
        using var app = new TestApp { Permissions = [] };
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url(path));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Spec", "PPL-1")]
    [Trait("Spec", "PPL-2")]
    public async Task AMember_CreatesTheirOwnProfileOnce_NamedByTheirClaims()
    {
        using var app = new TestApp(
            ("Auth:DevUser:Username", "jdoe"),
            ("Auth:DevUser:FirstName", "Jane"),
            ("Auth:DevUser:LastName", "Doe"))
        {
            Permissions = [],
        };
        using var client = app.CreateClient();

        using var created = await client.PostAsJsonAsync(People, new UpdatePersonRequest("JD", "CH-ZH"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var person = (await created.Content.ReadFromJsonAsync<PersonDto>()).ShouldNotBeNull();
        person.Username.ShouldBe("jdoe");
        person.DisplayName.ShouldBe("JD");
        person.HomeCity.ShouldBe("CH-ZH");

        var stored = app.Query(db => db.People.Single());
        (stored.FirstName, stored.LastName).ShouldBe(("Jane", "Doe"));

        using var again = await client.PostAsJsonAsync(People, new UpdatePersonRequest("JD again", null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        app.Query(db => db.People.Count()).ShouldBe(1);
    }

    [Fact]
    [Trait("Spec", "PPL-5")]
    public async Task APeopleManager_CreatesUpdatesAndDeletesSomeoneElsesProfile()
    {
        using var app = new TestApp { Permissions = [Permissions.PeopleManage] };
        using var client = app.CreateClient();

        using var created = await client.PostAsJsonAsync(
            Url("/api/admin/people"), new CreatePersonRequest("someone", "Some", "One", "Someone", null));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var person = (await created.Content.ReadFromJsonAsync<PersonDto>()).ShouldNotBeNull();
        person.Username.ShouldBe("someone");

        using var updated = await client.PutAsJsonAsync(
            Url($"/api/admin/people/{person.Id}"), new UpdatePersonRequest("Renamed", "CH-ZH"));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = app.Query(db => db.People.Single());
        (stored.DisplayName, stored.HomeCity).ShouldBe(("Renamed", "CH-ZH"));

        using var deleted = await client.DeleteAsync(Url($"/api/admin/people/{person.Id}"));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        app.Query(db => db.People.Any()).ShouldBeFalse();
    }

    // Three people with 3, 1 and 0 vacation days in 2026, each a run of weekdays.
    private static void SeedVacations(TestApp app) => app.Seed(db =>
    {
        var most = Rows.Person("most");
        var some = Rows.Person("some");
        var none = Rows.Person("none");
        db.People.AddRange(most, some, none);
        db.Absences.AddRange(
            Rows.Vacation(most, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)),
            Rows.Vacation(some, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 9)));
    });

    [Theory]
    [Trait("Spec", "PPL-8")]
    [InlineData("vacation")]
    [InlineData("other")]
    public async Task SortingByAnAbsenceTotal_WithoutReportsRead_IsForbidden(string column)
    {
        using var app = new TestApp { Permissions = [Permissions.PeopleManage] };
        SeedVacations(app);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(Url($"/api/people/paged?sortBy={column}&year=2026"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [Trait("Spec", "PPL-8")]
    [InlineData("asc", new[] { "none", "some", "most" })]
    [InlineData("desc", new[] { "most", "some", "none" })]
    public async Task SortingByAnAbsenceTotal_WithReportsRead_OrdersByIt(string direction, string[] expected)
    {
        using var app = new TestApp { Permissions = [Permissions.ReportsRead] };
        SeedVacations(app);
        using var client = app.CreateClient();

        var page = (await client.GetFromJsonAsync<PeoplePageResponse>(
            Url($"/api/people/paged?sortBy=vacation&sortDir={direction}&year=2026"))).ShouldNotBeNull();

        page.People.Select(p => p.Username).ShouldBe(expected);
    }
}
