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
