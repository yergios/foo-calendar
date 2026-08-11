using System.Net;
using System.Text;

using Api.Data.Models;
using Api.Dtos;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class BankHolidayServiceTests : SqliteTestBase
{
    private const string Actor = "admin";

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private BankHolidayService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder = null)
    {
        responder ??= _ => JsonResponse("[]");
        var http = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://fake.nager.test/"),
        };
        return new BankHolidayService(Db, http, NullLogger<BankHolidayService>.Instance);
    }

    [Fact]
    public async Task GetForRegionsAsync_IncludesNationwideHolidaysWhenQueryingASpecificRegion()
    {
        var sut = CreateService();
        Db.BankHolidays.AddRange(
            new BankHoliday { Region = "CH", Date = new DateOnly(2026, 8, 1), Name = "Swiss National Day", Source = HolidaySource.Nager, CreatedBy = "nager-sync" },
            new BankHoliday { Region = "CH-ZH", Date = new DateOnly(2026, 4, 13), Name = "Sechselauten", Source = HolidaySource.Nager, CreatedBy = "nager-sync" },
            new BankHoliday { Region = "CH-BE", Date = new DateOnly(2026, 1, 2), Name = "Berchtoldstag", Source = HolidaySource.Nager, CreatedBy = "nager-sync" });
        Db.SaveChanges();

        var result = await sut.GetForRegionsAsync(["CH-ZH"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        result.Holidays.Select(h => h.Region).ShouldBe(["CH", "CH-ZH"], ignoreOrder: true);
    }

    [Fact]
    public async Task AddManualAsync_WhenDuplicateRegionAndDate_Throws()
    {
        var sut = CreateService();
        await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor);

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Another Name"), Actor));
    }

    [Fact]
    public async Task AddManualAsync_CreatesHolidayWithManualSource()
    {
        var sut = CreateService();

        var created = await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor);

        created.Id.ShouldBeGreaterThan(0);
        created.Source.ShouldBe(HolidaySource.Manual);
    }

    [Fact]
    public async Task AddManualAsync_RecordsWhoCreatedIt()
    {
        var sut = CreateService();

        var created = await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor);

        var holiday = Db.BankHolidays.Single(h => h.Id == created.Id);
        holiday.CreatedBy.ShouldBe(Actor);
        holiday.CreatedAt.ShouldNotBe(default);
        holiday.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateManualAsync_WhenHolidayDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => sut.UpdateManualAsync(999, new AddManualHolidayRequest("CH", new DateOnly(2026, 1, 1), "X"), Actor));
    }

    [Fact]
    public async Task UpdateManualAsync_WhenHolidayIsNagerSourced_Throws()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 1, 1), Name = "New Year", Source = HolidaySource.Nager, CreatedBy = "nager-sync" };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 1, 1), "Renamed"), Actor));
    }

    [Fact]
    public async Task UpdateManualAsync_WhenConflictingWithAnotherHoliday_Throws()
    {
        Db.BankHolidays.Add(new BankHoliday { Region = "CH", Date = new DateOnly(2026, 5, 1), Name = "Existing", Source = HolidaySource.Manual, CreatedBy = Actor });
        var editable = new BankHoliday { Region = "CH-ZH", Date = new DateOnly(2026, 6, 1), Name = "Mine", Source = HolidaySource.Manual, CreatedBy = Actor };
        Db.BankHolidays.Add(editable);
        Db.SaveChanges();
        var sut = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateManualAsync(editable.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 5, 1), "Renamed"), Actor));
    }

    [Fact]
    public async Task UpdateManualAsync_UpdatesTheHolidayWhenValid()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 5, 1), Name = "Old", Source = HolidaySource.Manual, CreatedBy = Actor };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        var updated = await sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 2), "New"), Actor);

        updated.Region.ShouldBe("CH-ZH");
        updated.Date.ShouldBe(new DateOnly(2026, 5, 2));
        updated.Name.ShouldBe("New");
    }

    [Fact]
    public async Task UpdateManualAsync_RecordsWhoUpdatedIt()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 5, 1), Name = "Old", Source = HolidaySource.Manual, CreatedBy = "alice" };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 5, 1), "New"), "bob");

        var updated = Db.BankHolidays.Single(h => h.Id == holiday.Id);
        updated.CreatedBy.ShouldBe("alice");
        updated.UpdatedBy.ShouldBe("bob");
        updated.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteManualAsync_WhenHolidayDoesNotExist_ThrowsKeyNotFoundException()
    {
        var sut = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(() => sut.DeleteManualAsync(999));
    }

    [Fact]
    public async Task DeleteManualAsync_WhenHolidayIsNagerSourced_Throws()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 1, 1), Name = "New Year", Source = HolidaySource.Nager, CreatedBy = "nager-sync" };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(() => sut.DeleteManualAsync(holiday.Id));
    }

    [Fact]
    public async Task DeleteManualAsync_RemovesAManualHoliday()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 5, 1), Name = "Custom", Source = HolidaySource.Manual, CreatedBy = Actor };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.DeleteManualAsync(holiday.Id);

        Db.BankHolidays.Any(h => h.Id == holiday.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task SyncCountriesAsync_AddsGlobalAndCountySpecificHolidaysFromNagerResponse()
    {
        const string json = """
            [
              { "date": "2026-01-01", "localName": "Neujahr", "name": "New Year's Day", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] },
              { "date": "2026-04-13", "localName": "Sechselauten", "name": "Sechselauten", "countryCode": "CH", "global": false, "counties": ["CH-ZH", "CH-BE"], "types": ["Public"] }
            ]
            """;
        var sut = CreateService(_ => JsonResponse(json));

        var failed = await sut.SyncCountriesAsync(["CH"], 2026);

        failed.ShouldBeEmpty();
        var holidays = Db.BankHolidays.ToList();
        holidays.ShouldContain(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1) && h.Source == HolidaySource.Nager);
        holidays.ShouldContain(h => h.Region == "CH-ZH" && h.Date == new DateOnly(2026, 4, 13));
        holidays.ShouldContain(h => h.Region == "CH-BE" && h.Date == new DateOnly(2026, 4, 13));
    }

    [Fact]
    public async Task SyncCountriesAsync_NeverOverwritesAManuallyAddedHoliday()
    {
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2026, 1, 1),
            Name = "Our Custom Name",
            Source = HolidaySource.Manual,
            CreatedBy = Actor,
        });
        Db.SaveChanges();

        const string json = """
            [{ "date": "2026-01-01", "localName": "Neujahr", "name": "New Year's Day", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }]
            """;
        var sut = CreateService(_ => JsonResponse(json));

        await sut.SyncCountriesAsync(["CH"], 2026);

        var holiday = Db.BankHolidays.Single(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1));
        holiday.Name.ShouldBe("Our Custom Name");
        holiday.Source.ShouldBe(HolidaySource.Manual);
    }

    [Fact]
    public async Task SyncCountriesAsync_RefreshesNameForAnExistingNagerHoliday()
    {
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2026, 1, 1),
            Name = "Old Name",
            Source = HolidaySource.Nager,
            CreatedBy = "nager-sync",
        });
        Db.SaveChanges();

        const string json = """
            [{ "date": "2026-01-01", "localName": "Neujahr", "name": "New Name", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }]
            """;
        var sut = CreateService(_ => JsonResponse(json));

        await sut.SyncCountriesAsync(["CH"], 2026);

        Db.BankHolidays.Single(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1)).Name.ShouldBe("New Name");
    }

    [Fact]
    public async Task SyncCountriesAsync_DedupesDuplicateEntriesWithinTheSameResponse()
    {
        const string json = """
            [
              { "date": "2026-01-01", "localName": "A", "name": "First Wins", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] },
              { "date": "2026-01-01", "localName": "B", "name": "Second", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }
            ]
            """;
        var sut = CreateService(_ => JsonResponse(json));

        await sut.SyncCountriesAsync(["CH"], 2026);

        var matches = Db.BankHolidays.Where(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1)).ToList();
        var holiday = matches.ShouldHaveSingleItem();
        holiday.Name.ShouldBe("First Wins");
    }

    [Fact]
    public async Task SyncCountriesAsync_WhenOneCountryFails_StillSyncsTheOthersAndReportsTheFailure()
    {
        var sut = CreateService(request =>
        {
            var isFailingCountry = request.RequestUri!.ToString().Contains("/2026/DE");
            return isFailingCountry
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : JsonResponse("""[{ "date": "2026-01-01", "localName": "A", "name": "New Year", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }]""");
        });

        var failed = await sut.SyncCountriesAsync(["CH", "DE"], 2026);

        failed.ShouldBe(["DE"]);
        Db.BankHolidays.Any(h => h.Region == "CH").ShouldBeTrue();
    }

    [Fact]
    public async Task GetForRegionsAsync_ProjectsARecurringHolidayIntoAYearOtherThanItsStoredOne()
    {
        var sut = CreateService();
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2020, 3, 15),
            Name = "Founder's Day",
            Source = HolidaySource.Manual,
            IsRecurringAnnually = true,
            CreatedBy = Actor,
        });
        Db.SaveChanges();

        var result = await sut.GetForRegionsAsync(["CH"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var occurrence = result.Holidays.ShouldHaveSingleItem();
        occurrence.Date.ShouldBe(new DateOnly(2026, 3, 15));
        occurrence.Name.ShouldBe("Founder's Day");
    }

    [Fact]
    public async Task GetForRegionsAsync_SkipsALeapDayRecurrenceOnANonLeapYear()
    {
        var sut = CreateService();
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2020, 2, 29),
            Name = "Leap Day Holiday",
            Source = HolidaySource.Manual,
            IsRecurringAnnually = true,
            CreatedBy = Actor,
        });
        Db.SaveChanges();

        var result = await sut.GetForRegionsAsync(["CH"], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        result.Holidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task AddManualAsync_WhenCollidingWithARecurringHolidayInADifferentYear_Throws()
    {
        var sut = CreateService();
        await sut.AddManualAsync(new AddManualHolidayRequest("CH", new DateOnly(2020, 3, 15), "Founder's Day", IsRecurringAnnually: true), Actor);

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.AddManualAsync(new AddManualHolidayRequest("CH", new DateOnly(2026, 3, 15), "Clashing Day"), Actor));
    }

    [Fact]
    public async Task UpdateManualAsync_WhenCollidingWithARecurringHolidayInADifferentYear_Throws()
    {
        var recurring = new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2020, 3, 15),
            Name = "Founder's Day",
            Source = HolidaySource.Manual,
            IsRecurringAnnually = true,
            CreatedBy = Actor,
        };
        Db.BankHolidays.Add(recurring);
        var editable = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 6, 1), Name = "Mine", Source = HolidaySource.Manual, CreatedBy = Actor };
        Db.BankHolidays.Add(editable);
        Db.SaveChanges();
        var sut = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateManualAsync(editable.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 3, 15), "Renamed"), Actor));
    }

    [Fact]
    public async Task SyncCountriesAsync_DoesNotDuplicateARecurringManualOverrideInADifferentYear()
    {
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2020, 1, 1),
            Name = "Our Custom Name",
            Source = HolidaySource.Manual,
            IsRecurringAnnually = true,
            CreatedBy = Actor,
        });
        Db.SaveChanges();

        const string json = """
            [{ "date": "2026-01-01", "localName": "Neujahr", "name": "New Year's Day", "countryCode": "CH", "global": true, "counties": null, "types": ["Public"] }]
            """;
        var sut = CreateService(_ => JsonResponse(json));

        await sut.SyncCountriesAsync(["CH"], 2026);

        var matches = Db.BankHolidays.Where(h => h.Region == "CH").ToList();
        var holiday = matches.ShouldHaveSingleItem();
        holiday.Name.ShouldBe("Our Custom Name");
        holiday.Date.ShouldBe(new DateOnly(2020, 1, 1));
    }
}
