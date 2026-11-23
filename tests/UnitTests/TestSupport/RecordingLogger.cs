using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace UnitTests.TestSupport;

/// <summary>Keeps every entry logged to it, for a test to assert that something was
/// reported. Thread-safe, since background jobs log from threads of their own.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly ConcurrentQueue<Entry> entries = new();

    public IReadOnlyCollection<Entry> Entries => entries;

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
