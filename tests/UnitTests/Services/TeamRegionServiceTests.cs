using Api.Configuration;
using Api.Data.Models;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class TeamRegionServiceTests : SqliteTestBase
{
    /// <summary>Produces nothing. Every test here is about which regions are kept or
    /// dropped, which is decided before a source is ever consulted.</summary>
    private sealed class EmptyHolidaySource : IHolidaySource
    {
        public HolidaySource Kind => HolidaySource.Nager;

        public Task<IReadOnlyList<SourceHoliday>> GetAsync(
            string countryCode, int year, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourceHoliday>>([]);

        public Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetRegionsAsync(
            string countryCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private TeamRegionService CreateService(string homeRegion = "CH-ZH") =>
        new(Db,
            new BankHolidayService(
                Db, new EmptyHolidaySource(), new RecordingAuditLog(), NullLogger<BankHolidayService>.Instance),
            Options.Create(new TeamOptions { HomeRegion = homeRegion }),
            NullLogger<TeamRegionService>.Instance);

    private void AddPerson(string username, string homeCity) =>
        Db.People.Add(new Person
        {
            Username = username,
            DisplayName = username,
            HomeCity = homeCity,
            CreatedBy = "admin",
        });

    private void AddHoliday(string region, DateOnly date, HolidaySource source) =>
        Db.BankHolidays.Add(new BankHoliday
        {
            Region = region,
            Date = date,
            Name = $"{region} holiday",
            Source = source,
            CreatedBy = source == HolidaySource.Manual ? "admin" : "nager-sync",
        });

    [Fact]
    public async Task InUseAsync_IncludesTheHomeRegionWhenNobodyIsInIt()
    {
        var sut = CreateService();

        var regions = await sut.InUseAsync();

        regions.ShouldBe(["CH-ZH"]);
    }

    [Fact]
    public async Task InUseAsync_ReturnsEachOccupiedRegionOnce()
    {
        AddPerson("ana", "ES-MD");
        AddPerson("ben", "ES-MD");
        AddPerson("cleo", "RO-B");
        Db.SaveChanges();
        var sut = CreateService();

        var regions = await sut.InUseAsync();

        regions.ShouldBe(["CH-ZH", "ES-MD", "RO-B"], ignoreOrder: true);
    }

    [Fact]
    public async Task InUseAsync_IgnoresPeopleWithNoRegionSet()
    {
        AddPerson("ana", null);
        Db.SaveChanges();
        var sut = CreateService();

        var regions = await sut.InUseAsync();

        regions.ShouldBe(["CH-ZH"]);
    }

    [Fact]
    public async Task PruneIfUnusedAsync_RemovesTheRegionAndItsCountryOnceNobodyIsLeft()
    {
        AddHoliday("ES", new DateOnly(2026, 1, 1), HolidaySource.Nager);
        AddHoliday("ES-MD", new DateOnly(2026, 5, 2), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync("ES-MD");

        Db.BankHolidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task PruneIfUnusedAsync_RemovesManuallyAddedHolidaysToo()
    {
        AddHoliday("ES-MD", new DateOnly(2026, 5, 2), HolidaySource.Manual);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync("ES-MD");

        Db.BankHolidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task PruneIfUnusedAsync_KeepsTheCountryWhileAnotherRegionOfItIsOccupied()
    {
        // The last person in Madrid leaving doesn't take Spain's national holidays with
        // them: everyone still in Asturias observes those.
        AddPerson("ana", "ES-AS");
        AddHoliday("ES", new DateOnly(2026, 1, 1), HolidaySource.Nager);
        AddHoliday("ES-MD", new DateOnly(2026, 5, 2), HolidaySource.Nager);
        AddHoliday("ES-AS", new DateOnly(2026, 9, 8), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync("ES-MD");

        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["ES", "ES-AS"], ignoreOrder: true);
    }

    [Fact]
    public async Task PruneIfUnusedAsync_KeepsTheRegionWhileSomeoneIsStillInIt()
    {
        AddPerson("ana", "ES-MD");
        AddHoliday("ES-MD", new DateOnly(2026, 5, 2), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync("ES-MD");

        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["ES-MD"]);
    }

    [Fact]
    public async Task PruneIfUnusedAsync_NeverRemovesTheHomeRegion()
    {
        AddHoliday("CH", new DateOnly(2026, 8, 1), HolidaySource.Nager);
        AddHoliday("CH-ZH", new DateOnly(2026, 4, 13), HolidaySource.Manual);
        Db.SaveChanges();
        var sut = CreateService(homeRegion: "CH-ZH");

        await sut.PruneIfUnusedAsync("CH-ZH");

        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["CH", "CH-ZH"], ignoreOrder: true);
    }

    [Fact]
    public async Task PruneIfUnusedAsync_KeepsTheHomeCountryWhenAnotherOfItsRegionsEmpties()
    {
        // Nobody is left in Bern, but Zürich is home, so Switzerland's own rows stay.
        AddHoliday("CH", new DateOnly(2026, 8, 1), HolidaySource.Nager);
        AddHoliday("CH-BE", new DateOnly(2026, 1, 2), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService(homeRegion: "CH-ZH");

        await sut.PruneIfUnusedAsync("CH-BE");

        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["CH"]);
    }

    [Fact]
    public async Task PruneIfUnusedAsync_WhenSomeoneOnlyEverPickedACountry_RemovesItOnce()
    {
        AddHoliday("ES", new DateOnly(2026, 1, 1), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync("ES");

        Db.BankHolidays.ShouldBeEmpty();
    }

    [Fact]
    public async Task PruneIfUnusedAsync_WhenTheRegionIsNullOrBlank_DoesNothing()
    {
        AddHoliday("ES", new DateOnly(2026, 1, 1), HolidaySource.Nager);
        Db.SaveChanges();
        var sut = CreateService();

        await sut.PruneIfUnusedAsync(null);
        await sut.PruneIfUnusedAsync("");

        Db.BankHolidays.Select(h => h.Region).ToList().ShouldBe(["ES"]);
    }
}
