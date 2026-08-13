using System.Net;
using System.Text;

using Api.Data.Models;
using Api.Services;

using Shouldly;

namespace UnitTests.Services;

public sealed class NagerHolidaySourceTests
{
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static NagerHolidaySource CreateSource(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://fake.nager.test/"),
        });

    [Fact]
    public async Task GetAsync_MapsAGlobalHolidayToTheCountryCode()
    {
        const string json = """
            [{ "date": "2026-01-01", "localName": "Neujahr", "name": "New Year's Day", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }]
            """;
        var sut = CreateSource(_ => JsonResponse(json));

        var holidays = await sut.GetAsync("CH", 2026);

        var holiday = holidays.ShouldHaveSingleItem();
        holiday.Region.ShouldBe("CH");
        holiday.Date.ShouldBe(new DateOnly(2026, 1, 1));
        holiday.Name.ShouldBe("New Year's Day");
    }

    [Fact]
    public async Task GetAsync_ExpandsANonGlobalHolidayToOneEntryPerCounty()
    {
        const string json = """
            [{ "date": "2026-04-13", "localName": "Sechselauten", "name": "Sechselauten", "countryCode": "CH", "global": false, "counties": ["CH-ZH", "CH-BE"], "types": ["Public"] }]
            """;
        var sut = CreateSource(_ => JsonResponse(json));

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.Select(h => h.Region).ShouldBe(["CH-ZH", "CH-BE"], ignoreOrder: true);
        holidays.ShouldAllBe(h => h.Date == new DateOnly(2026, 4, 13));
    }

    [Fact]
    public async Task GetAsync_SkipsANonGlobalHolidayWithNoCounties()
    {
        const string json = """
            [{ "date": "2026-04-13", "localName": "Nowhere", "name": "Nowhere", "countryCode": "CH", "global": false, "counties": null, "types": ["Public"] }]
            """;
        var sut = CreateSource(_ => JsonResponse(json));

        var holidays = await sut.GetAsync("CH", 2026);

        holidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_RequestsTheYearAndCountryAsked()
    {
        Uri requested = null;
        var sut = CreateSource(request =>
        {
            requested = request.RequestUri;
            return JsonResponse("[]");
        });

        await sut.GetAsync("ES", 2027);

        requested!.ToString().ShouldEndWith("PublicHolidays/2027/ES");
    }

    [Fact]
    public async Task GetAsync_WhenTheApiFails_Throws()
    {
        var sut = CreateSource(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Should.ThrowAsync<HttpRequestException>(() => sut.GetAsync("CH", 2026));
    }

    [Fact]
    public void Kind_IsNager()
    {
        CreateSource(_ => JsonResponse("[]")).Kind.ShouldBe(HolidaySource.Nager);
    }

    [Fact]
    public async Task GetCountriesAsync_ReturnsTheAvailableCountryCodes()
    {
        var sut = CreateSource(_ => JsonResponse("""
            [{ "countryCode": "CH", "name": "Switzerland" }, { "countryCode": "ES", "name": "Spain" }]
            """));

        var countries = await sut.GetCountriesAsync();

        countries.ShouldBe(["CH", "ES"]);
    }

    // The enumeration cache is static, so these use country codes no other test touches.
    // Sharing one would mean whichever test ran first decided the answer for the others.
    [Fact]
    public async Task GetRegionsAsync_ReturnsDistinctCountiesFromAYearOfHolidays()
    {
        var sut = CreateSource(_ => JsonResponse("""
            [
              { "date": "2026-01-01", "name": "Nationwide", "countryCode": "QA", "global": true, "counties": null },
              { "date": "2026-04-13", "name": "Regional", "countryCode": "QA", "global": false, "counties": ["QA-1", "QA-2"] },
              { "date": "2026-09-08", "name": "Also regional", "countryCode": "QA", "global": false, "counties": ["QA-2"] }
            ]
            """));

        var regions = await sut.GetRegionsAsync("QA");

        regions.ShouldBe(["QA-1", "QA-2"]);
    }

    [Fact]
    public async Task GetRegionsAsync_WhenNoHolidayIsRegional_ReturnsNothing()
    {
        var sut = CreateSource(_ => JsonResponse("""
            [{ "date": "2026-01-01", "name": "Nationwide", "countryCode": "QB", "global": true, "counties": null }]
            """));

        var regions = await sut.GetRegionsAsync("QB");

        regions.ShouldBeEmpty();
    }
}
