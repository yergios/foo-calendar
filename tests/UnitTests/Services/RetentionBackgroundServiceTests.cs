using System.Threading.Channels;

using Api.Configuration;
using Api.Interfaces;
using Api.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Services;

public sealed class RetentionBackgroundServiceTests
{
    /// <summary>Fails its first sweep and succeeds after, telling the test about each.
    /// </summary>
    private sealed class FailingOnceRetentionService : IRetentionService
    {
        private int sweeps;

        public Channel<int> Sweeps { get; } = Channel.CreateUnbounded<int>();

        public Task<RetentionSweepResult> SweepAsync(CancellationToken ct = default)
        {
            var sweep = Interlocked.Increment(ref sweeps);
            Sweeps.Writer.TryWrite(sweep);

            return sweep == 1
                ? throw new InvalidOperationException("The database is locked.")
                : Task.FromResult(new RetentionSweepResult(0, 0));
        }
    }

    [Fact]
    [Trait("Spec", "PRIV-3")]
    public async Task AFailedSweep_IsLogged_AndTheNextTickSweepsAgain()
    {
        var retention = new FailingOnceRetentionService();
        var logger = new RecordingLogger<RetentionBackgroundService>();
        await using var services = new ServiceCollection()
            .AddSingleton<IRetentionService>(retention)
            .BuildServiceProvider();
        using var sut = new RetentionBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RetentionOptions { SweepInterval = TimeSpan.FromMilliseconds(50) }),
            logger);

        await sut.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await retention.Sweeps.Reader.ReadAsync(timeout.Token) < 2)
        {
            // Waiting for the sweep after the failed one.
        }

        sut.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse();
        await sut.StopAsync(CancellationToken.None);

        var error = logger.Entries.Where(e => e.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeOfType<InvalidOperationException>();
    }
}
