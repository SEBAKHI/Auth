using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// A logger that keeps every entry as it would be rendered, so a test can assert on the exact
/// line an operator searches for. Safe to write from several threads.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>The entries written so far, oldest first.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Enqueue(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
}

/// <summary>One rendered log entry.</summary>
public sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);
