using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class BankHolidayServiceTests : SqliteTestBase
{
    private const string Actor = "admin";

    /// <summary>Where the team is, as the endpoints work it out before calling in. "CH"
    /// itself is fair game without being listed, since it is the country of both.</summary>
    private static readonly string[] Tracked = ["CH-ZH", "CH-BE"];

    /// <summary>Stands in for whichever IHolidaySource a deployment configures. What the
    /// service does with the holidays is the same either way, so these tests hand it a
    /// list directly instead of going through a source's own wire format.</summary>
    private sealed class StubHolidaySource(
        Func<string, int, IReadOnlyList<SourceHoliday>> responder,
        HolidaySource kind = HolidaySource.Nager) : IHolidaySource
    {
        public HolidaySource Kind => kind;

        public Task<IReadOnlyList<SourceHoliday>> GetAsync(
            string countryCode, int year, CancellationToken ct = default) =>
            Task.FromResult(responder(countryCode, year));

        // Enumeration only backs the region picker, which none of these tests go through.
        public Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetRegionsAsync(
            string countryCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private readonly RecordingAuditLog audit = new();

    private BankHolidayService CreateService(
        Func<string, int, IReadOnlyList<SourceHoliday>> responder = null,
        HolidaySource kind = HolidaySource.Nager) =>
        new(Db,
            new StubHolidaySource(responder ?? ((_, _) => []), kind),
            audit,
            NullLogger<BankHolidayService>.Instance);

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
        await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor, Tracked);

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Another Name"), Actor, Tracked));
    }

    [Fact]
    public async Task AddManualAsync_CreatesHolidayWithManualSource()
    {
        var sut = CreateService();

        var created = await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor, Tracked);

        created.Id.ShouldBeGreaterThan(0);
        created.Source.ShouldBe(HolidaySource.Manual);
    }

    [Fact]
    public async Task AddManualAsync_RecordsWhoCreatedIt()
    {
        var sut = CreateService();

        var created = await sut.AddManualAsync(new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor, Tracked);

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
            () => sut.UpdateManualAsync(999, new AddManualHolidayRequest("CH", new DateOnly(2026, 1, 1), "X"), Actor, Tracked));
    }

    [Fact]
    public async Task UpdateManualAsync_WhenHolidayIsNagerSourced_Throws()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 1, 1), Name = "New Year", Source = HolidaySource.Nager, CreatedBy = "nager-sync" };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 1, 1), "Renamed"), Actor, Tracked));
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
            () => sut.UpdateManualAsync(editable.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 5, 1), "Renamed"), Actor, Tracked));
    }

    [Fact]
    public async Task UpdateManualAsync_UpdatesTheHolidayWhenValid()
    {
        var holiday = new BankHoliday { Region = "CH", Date = new DateOnly(2026, 5, 1), Name = "Old", Source = HolidaySource.Manual, CreatedBy = Actor };
        Db.BankHolidays.Add(holiday);
        Db.SaveChanges();
        var sut = CreateService();

        var updated = await sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 2), "New"), Actor, Tracked);

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

        await sut.UpdateManualAsync(holiday.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 5, 1), "New"), "bob", Tracked);

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
    public async Task SyncRegionsAsync_AddsNationwideAndRegionalHolidaysFromTheSource()
    {
        var sut = CreateService((_, _) =>
        [
            new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year's Day"),
            new SourceHoliday("CH-ZH", new DateOnly(2026, 4, 13), "Sechselauten"),
            new SourceHoliday("CH-BE", new DateOnly(2026, 4, 13), "Sechselauten"),
        ]);

        var failed = await sut.SyncRegionsAsync(["CH-ZH", "CH-BE"], 2026);

        failed.ShouldBeEmpty();
        var holidays = Db.BankHolidays.ToList();
        holidays.ShouldContain(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1) && h.Source == HolidaySource.Nager);
        holidays.ShouldContain(h => h.Region == "CH-ZH" && h.Date == new DateOnly(2026, 4, 13));
        holidays.ShouldContain(h => h.Region == "CH-BE" && h.Date == new DateOnly(2026, 4, 13));
    }

    [Fact]
    public async Task SyncRegionsAsync_SkipsSubdivisionsNobodyIsIn()
    {
        // The source answers for the whole country whatever is asked of it, so filtering
        // what comes back is the only thing keeping the table to regions the team occupies.
        var sut = CreateService((_, _) =>
        [
            new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year's Day"),
            new SourceHoliday("CH-ZH", new DateOnly(2026, 4, 13), "Sechselauten"),
            new SourceHoliday("CH-BE", new DateOnly(2026, 4, 13), "Sechselauten"),
        ]);

        await sut.SyncRegionsAsync(["CH-ZH"], 2026);

        var regions = Db.BankHolidays.Select(h => h.Region).ToList();
        regions.ShouldBe(["CH", "CH-ZH"], ignoreOrder: true);
    }

    [Fact]
    public async Task SyncRegionsAsync_FetchesOnceForSeveralRegionsOfTheSameCountry()
    {
        var fetches = 0;
        var sut = CreateService((_, _) =>
        {
            fetches++;
            return [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year's Day")];
        });

        await sut.SyncRegionsAsync(["CH-ZH", "CH-BE", "CH-GE"], 2026);

        fetches.ShouldBe(1);
    }

    [Fact]
    public async Task SyncRegionsAsync_NeverOverwritesAManuallyAddedHoliday()
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

        var sut = CreateService((_, _) => [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year's Day")]);

        await sut.SyncRegionsAsync(["CH"], 2026);

        var holiday = Db.BankHolidays.Single(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1));
        holiday.Name.ShouldBe("Our Custom Name");
        holiday.Source.ShouldBe(HolidaySource.Manual);
    }

    [Fact]
    public async Task SyncRegionsAsync_RefreshesNameForAnExistingSyncedHoliday()
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

        var sut = CreateService((_, _) => [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Name")]);

        await sut.SyncRegionsAsync(["CH"], 2026);

        Db.BankHolidays.Single(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1)).Name.ShouldBe("New Name");
    }

    [Fact]
    public async Task SyncRegionsAsync_DedupesDuplicateEntriesWithinTheSameResponse()
    {
        var sut = CreateService((_, _) =>
        [
            new SourceHoliday("CH", new DateOnly(2026, 1, 1), "First Wins"),
            new SourceHoliday("CH", new DateOnly(2026, 1, 1), "Second"),
        ]);

        await sut.SyncRegionsAsync(["CH"], 2026);

        var matches = Db.BankHolidays.Where(h => h.Region == "CH" && h.Date == new DateOnly(2026, 1, 1)).ToList();
        var holiday = matches.ShouldHaveSingleItem();
        holiday.Name.ShouldBe("First Wins");
    }

    [Fact]
    public async Task SyncRegionsAsync_WhenOneCountryFails_StillSyncsTheOthersAndReportsTheFailure()
    {
        var sut = CreateService((country, _) => country == "DE"
            ? throw new HttpRequestException("the source is unreachable")
            : [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year")]);

        var failed = await sut.SyncRegionsAsync(["CH", "DE"], 2026);

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
        await sut.AddManualAsync(new AddManualHolidayRequest("CH", new DateOnly(2020, 3, 15), "Founder's Day", IsRecurringAnnually: true), Actor, Tracked);

        await Should.ThrowAsync<InvalidOperationException>(
            () => sut.AddManualAsync(new AddManualHolidayRequest("CH", new DateOnly(2026, 3, 15), "Clashing Day"), Actor, Tracked));
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
            () => sut.UpdateManualAsync(editable.Id, new AddManualHolidayRequest("CH", new DateOnly(2026, 3, 15), "Renamed"), Actor, Tracked));
    }

    [Fact]
    public async Task SyncRegionsAsync_DoesNotDuplicateARecurringManualOverrideInADifferentYear()
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

        var sut = CreateService((_, _) => [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year's Day")]);

        await sut.SyncRegionsAsync(["CH"], 2026);

        var matches = Db.BankHolidays.Where(h => h.Region == "CH").ToList();
        var holiday = matches.ShouldHaveSingleItem();
        holiday.Name.ShouldBe("Our Custom Name");
        holiday.Date.ShouldBe(new DateOnly(2020, 1, 1));
    }

    [Fact]
    public async Task SyncRegionsAsync_StampsRowsWithTheConfiguredSource()
    {
        var sut = CreateService(
            (_, _) => [new SourceHoliday("CH", new DateOnly(2026, 12, 24), "Office closed")],
            HolidaySource.File);

        await sut.SyncRegionsAsync(["CH"], 2026);

        var holiday = Db.BankHolidays.ToList().ShouldHaveSingleItem();
        holiday.Source.ShouldBe(HolidaySource.File);
        holiday.CreatedBy.ShouldBe("file-sync");
    }

    [Fact]
    public async Task SyncRegionsAsync_RelabelsAnExistingRowWhenTheSourceChanges()
    {
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = "CH",
            Date = new DateOnly(2026, 1, 1),
            Name = "New Year",
            Source = HolidaySource.Nager,
            CreatedBy = "nager-sync",
        });
        Db.SaveChanges();
        var sut = CreateService(
            (_, _) => [new SourceHoliday("CH", new DateOnly(2026, 1, 1), "New Year")],
            HolidaySource.File);

        await sut.SyncRegionsAsync(["CH"], 2026);

        var holiday = Db.BankHolidays.ToList().ShouldHaveSingleItem();
        holiday.Source.ShouldBe(HolidaySource.File);
        holiday.UpdatedBy.ShouldBe("file-sync");
    }

    [Fact]
    public async Task SyncRegionsAsync_WhenACountryFails_ReportsEveryRegionOfIt()
    {
        // A source deals in whole countries, so one failed fetch takes every region of that
        // country with it. Reporting only the country would leave an admin no way to see
        // which of their regions actually went unsynced.
        var sut = CreateService((_, _) => throw new HttpRequestException("unreachable"));

        var failed = await sut.SyncRegionsAsync(["CH-ZH", "CH-BE"], 2026);

        failed.ShouldBe(["CH-ZH", "CH-BE"], ignoreOrder: true);
    }

    [Fact]
    public async Task RemoveForRegionsAsync_DeletesManuallyAddedHolidaysToo()
    {
        var sut = CreateService();
        await sut.AddManualAsync(
            new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor, Tracked);

        var removed = await sut.RemoveForRegionsAsync(["CH-ZH"]);

        removed.ShouldBe(1);
        Db.BankHolidays.Any(h => h.Region == "CH-ZH").ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveForRegionsAsync_MatchesRegionsExactlyRatherThanByCountry()
    {
        var sut = CreateService();
        Db.BankHolidays.AddRange(
            new BankHoliday { Region = "ES", Date = new DateOnly(2026, 1, 1), Name = "Año Nuevo", Source = HolidaySource.Nager, CreatedBy = "nager-sync" },
            new BankHoliday { Region = "ES-AS", Date = new DateOnly(2026, 9, 8), Name = "Día de Asturias", Source = HolidaySource.Nager, CreatedBy = "nager-sync" });
        Db.SaveChanges();

        // Dropping the country's own rows must leave a subdivision that is still occupied
        // alone, and vice versa. Prefix matching here would take out both.
        var removed = await sut.RemoveForRegionsAsync(["ES"]);

        removed.ShouldBe(1);
        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["ES-AS"]);
    }

    [Fact]
    public async Task AddManualAsync_WhenNobodyIsInTheRegion_Throws()
    {
        var sut = CreateService();

        await Should.ThrowAsync<ArgumentException>(
            () => sut.AddManualAsync(
                new AddManualHolidayRequest("ES-MD", new DateOnly(2026, 5, 2), "Dos de Mayo"), Actor, Tracked));
    }

    [Fact]
    public async Task AddManualAsync_AllowsTheCountryOfATrackedRegion()
    {
        var sut = CreateService();

        // Nobody is set to "CH" itself, but everyone in CH-ZH observes its nationwide days.
        var created = await sut.AddManualAsync(
            new AddManualHolidayRequest("CH", new DateOnly(2026, 5, 1), "Labour Day"), Actor, Tracked);

        created.Region.ShouldBe("CH");
    }

    [Fact]
    public async Task UpdateManualAsync_WhenMovedToAnUntrackedRegion_Throws()
    {
        var sut = CreateService();
        var created = await sut.AddManualAsync(
            new AddManualHolidayRequest("CH-ZH", new DateOnly(2026, 5, 1), "Custom Day"), Actor, Tracked);

        await Should.ThrowAsync<ArgumentException>(
            () => sut.UpdateManualAsync(
                created.Id,
                new AddManualHolidayRequest("ES-MD", new DateOnly(2026, 5, 1), "Custom Day"),
                Actor,
                Tracked));
    }
}
