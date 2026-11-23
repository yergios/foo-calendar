using System.Net;
using System.Net.Http.Json;

using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using IntegrationTests.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

namespace IntegrationTests;

/// <summary>POST /api/admin/bank-holidays/sync, the hand-triggered sync.</summary>
public sealed class HolidaySyncEndpointTests
{
    /// <summary>A source with one holiday for Switzerland whose fetch for Spain always
    /// fails, so one sync can report both outcomes.</summary>
    private sealed class SpainFailingSource : IHolidaySource
    {
        public HolidaySource Kind => HolidaySource.File;

        public Task<IReadOnlyList<SourceHoliday>> GetAsync(
            string countryCode, int year, CancellationToken ct = default) =>
            countryCode == "ES"
                ? throw new HttpRequestException("The source is down.")
                : Task.FromResult<IReadOnlyList<SourceHoliday>>(
                    [new SourceHoliday(countryCode, new DateOnly(year, 8, 1), "National Day")]);

        public Task<IReadOnlyList<string>> GetCountriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetRegionsAsync(string countryCode, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    // A source is configured, so the route syncs, but it is the stand-in above, and the
    // scheduled sync is taken out so nothing else asks it anything. The team is the home
    // region, CH-ZH, and one person in ES-MD.
    private static TestApp AppWithASource()
    {
        var app = new TestApp(("Holidays:Source", "File"), ("Holidays:File:Path", "unused.json"))
        {
            Permissions = [Permissions.HolidaysSync],
            ServiceOverrides = services =>
            {
                services.AddSingleton<IHolidaySource, SpainFailingSource>();
                var scheduled = services.Single(d => d.ImplementationType == typeof(BankHolidaySyncBackgroundService));
                services.Remove(scheduled);
            },
        };
        app.Seed(db => db.People.Add(Rows.Person("madrid", "ES-MD")));
        return app;
    }

    private static Uri Sync(string query) => new($"/api/admin/bank-holidays/sync?{query}", UriKind.Relative);

    private static List<AuditEntry> SyncEntries(TestApp app) =>
        app.Query(db => db.AuditEntries.Where(e => e.Action == AuditActions.BankHolidaysSynced).ToList());

    [Fact]
    [Trait("Spec", "HOL-11")]
    [Trait("Spec", "AUD-1")]
    public async Task SyncingEveryRegion_ListsWhatSyncedAndWhatFailed_AndIsAuditedOnce()
    {
        using var app = AppWithASource();
        using var client = app.CreateClient();

        using var response = await client.PostAsync(Sync("year=2026"), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SyncBankHolidaysResponse>()).ShouldNotBeNull();
        result.SyncedRegions.ShouldBe(["CH-ZH"]);
        result.FailedRegions.ShouldBe(["ES-MD"]);

        app.Query(db => db.BankHolidays.Select(h => h.Region).ToList()).ShouldBe(["CH"]);

        var entry = SyncEntries(app).ShouldHaveSingleItem();
        entry.Actor.ShouldBe("dev-user");
        entry.Target.ShouldBe("2026, all regions");
        entry.Details.ShouldBe("1 region(s) synced, failed: ES-MD");
    }

    [Fact]
    [Trait("Spec", "HOL-11")]
    [Trait("Spec", "AUD-1")]
    public async Task SyncingOneTrackedRegion_SyncsThatRegionAlone()
    {
        using var app = AppWithASource();
        using var client = app.CreateClient();

        using var response = await client.PostAsync(Sync("year=2026&region=CH-ZH"), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SyncBankHolidaysResponse>()).ShouldNotBeNull();
        result.SyncedRegions.ShouldBe(["CH-ZH"]);
        result.FailedRegions.ShouldBeEmpty();

        SyncEntries(app).ShouldHaveSingleItem().Target.ShouldBe("2026, CH-ZH");
    }

    [Fact]
    [Trait("Spec", "HOL-11")]
    public async Task SyncingAnUntrackedRegion_IsABadRequest_AndNotAudited()
    {
        using var app = AppWithASource();
        using var client = app.CreateClient();

        using var response = await client.PostAsync(Sync("year=2026&region=DE"), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        SyncEntries(app).ShouldBeEmpty();
    }

    // TestApp runs with Holidays:Source=None unless told otherwise.
    [Fact]
    [Trait("Spec", "HOL-2")]
    public async Task WithNoSource_NoSyncIsScheduled_AndAHandTriggeredOneIsAConflict()
    {
        using var app = new TestApp { Permissions = [Permissions.HolidaysSync] };
        using var client = app.CreateClient();

        app.Services.GetServices<IHostedService>().OfType<BankHolidaySyncBackgroundService>().ShouldBeEmpty();

        using var response = await client.PostAsync(Sync("year=2026"), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
