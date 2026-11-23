using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace IntegrationTests.TestSupport;

/// <summary>Keeps every message the app logs, and the exception logged with it, so a test
/// can assert that the app said something without scraping console output.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly ConcurrentQueue<Entry> entries = new();

    public IReadOnlyCollection<Entry> Entries => entries;

    public ILogger CreateLogger(string categoryName) => new Logger(entries);

    public void Dispose()
    {
        // Nothing to release: the entries are kept for the test to read after the app stops.
    }

    private sealed class Logger(ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
    }
}
