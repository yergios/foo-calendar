using Api.Configuration;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>Runs the retention sweep on a plain interval, shaped like
/// BankHolidaySyncBackgroundService. Runs once at startup too, so a deployment that has
/// just lowered a retention period doesn't wait a day to act on it.</summary>
public sealed class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // BackgroundService.StartAsync awaits this method until its first real suspension
        // point, and on SQLite there isn't one: Microsoft.Data.Sqlite has no true async I/O,
        // so every ExecuteDeleteAsync below completes inline. Without this yield the first
        // sweep - the big one, against a database that predates the setting - would run
        // inside startup, holding the write lock and delaying every hosted service
        // registered after this one while Kestrel is already accepting requests.
        await Task.Yield();

        using var timer = new PeriodicTimer(options.Value.SweepInterval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var retention = scope.ServiceProvider.GetRequiredService<IRetentionService>();
                await retention.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed sweep is data kept too long, which is worth a loud line, but it
                // is not worth taking the host down for: the next tick will try again.
                logger.LogError(ex, "Retention sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
