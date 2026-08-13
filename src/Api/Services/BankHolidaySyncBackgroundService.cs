using Api.Configuration;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class BankHolidaySyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HolidayOptions> holidayOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Defaults to weekly: public holiday data barely changes once published, so a
        // nightly poll would just hammer the source for nothing. A file source that gets
        // edited is the case worth shortening it for. Either way, an admin who needs it
        // sooner can use POST /api/admin/bank-holidays/sync.
        using var timer = new PeriodicTimer(holidayOptions.Value.SyncInterval);

        do
        {
            await SyncAllAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAllAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBankHolidayService>();
        var teamRegions = scope.ServiceProvider.GetRequiredService<ITeamRegionService>();

        var currentYear = DateTime.UtcNow.Year;

        // Re-read every run rather than captured once: people join and leave between ticks,
        // and this is what keeps the yearly rollover covering everywhere the team now is.
        var regions = await teamRegions.InUseAsync(ct);

        foreach (var year in new[] { currentYear, currentYear + 1 })
            await service.SyncRegionsAsync(regions, year, ct);
    }
}
