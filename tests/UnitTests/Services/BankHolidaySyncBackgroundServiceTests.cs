using Api.Configuration;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class BankHolidaySyncBackgroundServiceTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly string[] Tracked = ["CH-ZH", "ES-MD"];

    private readonly RecordingBankHolidayService bankHolidays = new();
    private readonly ServiceProvider services;

    public BankHolidaySyncBackgroundServiceTests()
    {
        services = new ServiceCollection()
            .AddSingleton<IBankHolidayService>(bankHolidays)
            .AddSingleton<ITeamRegionService>(new RecordingTeamRegionService { InUse = Tracked })
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync() => await services.DisposeAsync();

    private BankHolidaySyncBackgroundService CreateSut(TimeSpan interval) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new HolidayOptions { SyncInterval = interval }));

    private async Task<List<(IReadOnlyList<string> Regions, int Year)>> NextSyncsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(Patience);
        var syncs = new List<(IReadOnlyList<string> Regions, int Year)>();
        while (syncs.Count < count)
            syncs.Add(await bankHolidays.Syncs.Reader.ReadAsync(timeout.Token));
        return syncs;
    }

    // An interval far longer than the test, so anything synced came from the run at start.
    [Fact]
    [Trait("Spec", "HOL-2")]
    public async Task AtStartup_SyncsEveryTrackedRegionForThisYearAndNext()
    {
        using var sut = CreateSut(TimeSpan.FromDays(7));
        var thisYear = DateTime.UtcNow.Year;

        await sut.StartAsync(CancellationToken.None);
        var syncs = await NextSyncsAsync(2);
        await sut.StopAsync(CancellationToken.None);

        syncs.Select(s => s.Year).ShouldBe([thisYear, thisYear + 1]);
        syncs.ShouldAllBe(s => s.Regions.SequenceEqual(Tracked));
    }

    [Fact]
    [Trait("Spec", "HOL-2")]
    public async Task AfterEachInterval_SyncsAgain()
    {
        using var sut = CreateSut(TimeSpan.FromMilliseconds(50));
        var thisYear = DateTime.UtcNow.Year;

        await sut.StartAsync(CancellationToken.None);
        var syncs = await NextSyncsAsync(6);
        await sut.StopAsync(CancellationToken.None);

        syncs.Select(s => s.Year).ShouldBe([thisYear, thisYear + 1, thisYear, thisYear + 1, thisYear, thisYear + 1]);
    }
}
