using Api.Configuration;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>Fires the weekly "who's off" digest once a week, at a configured day and time
/// rather than a fixed interval from process start. Shaped like
/// BankHolidaySyncBackgroundService, but the delay to the next run is recomputed every
/// time instead of using a single PeriodicTimer interval, so a restart or a missed tick
/// self-corrects onto the next real occurrence of the target day/time instead of drifting.
/// </summary>
public sealed class WeeklyDigestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> emailOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var digest = emailOptions.Value.WeeklyDigest;
        if (!digest.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = DelayUntilNext(digest.DayOfWeek, digest.TimeOfDay, DateTime.Now);
            await Task.Delay(delay, stoppingToken);
            await RunAsync(stoppingToken);
        }
    }

    /// <summary>How long until the next occurrence of <paramref name="day"/> at
    /// <paramref name="timeOfDay"/>, treating an exact match on <paramref name="now"/> as
    /// already past, so the digest never fires twice for the same target instant.</summary>
    public static TimeSpan DelayUntilNext(DayOfWeek day, TimeSpan timeOfDay, DateTime now)
    {
        var daysUntil = ((int)day - (int)now.DayOfWeek + 7) % 7;
        var candidate = now.Date.AddDays(daysUntil).Add(timeOfDay);

        if (candidate <= now)
            candidate = candidate.AddDays(7);

        return candidate - now;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var digestService = scope.ServiceProvider.GetRequiredService<IWeeklyDigestService>();
        await digestService.SendAsync(ct);
    }
}
