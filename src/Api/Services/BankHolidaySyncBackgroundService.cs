using Api.Configuration;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

public sealed class BankHolidaySyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<RegionCatalogOptions> regionCatalog) : BackgroundService
{
    // Public holiday data barely changes once published, so a nightly poll would just
    // hammer Nager for nothing. Weekly is plenty to catch newly published years or rare
    // corrections. If you need it sooner, use POST /api/admin/bank-holidays/sync.
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

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

        var currentYear = DateTime.UtcNow.Year;
        var countries = regionCatalog.Value.Select(c => c.Code).ToList();

        foreach (var year in new[] { currentYear, currentYear + 1 })
            await service.SyncCountriesAsync(countries, year, ct);
    }
}
