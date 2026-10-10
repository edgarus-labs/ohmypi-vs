using System.Collections.Concurrent;

namespace Omp.Core.Tests.Support;

/// <summary>Records every message; an error argument is appended as <c>: message</c>.</summary>
public sealed class MemoryLogger : IOmpLogger
{
    private readonly ConcurrentQueue<LogRecord> _records = new();

    /// <summary>
    /// Initializes a new instance of the MemoryLogger class with a specified value indicating whether trace logging is enabled.
    /// </summary>
    /// <param name="trace">The trace.</param>
    public MemoryLogger(bool trace = false)
    {
        TraceEnabled = trace;
    }

    /// <summary>
    /// Gets or sets a value indicating whether trace enabled.
    /// </summary>
    public bool TraceEnabled { get; set; }

    /// <summary>
    /// Gets the collection of records.
    /// </summary>
    public IReadOnlyList<LogRecord> Records => _records.ToArray();

    /// <summary>
    /// Gets or sets the on warn.
    /// </summary>
    public Action<string, Exception?>? OnWarn { get; set; }

    /// <summary>
    /// Returns a newline-delimited string of log messages, optionally filtered by a specific severity level, including associated error details where available.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>The string result.</returns>
    public string Text(string? level = null) =>
        string.Join("\n", _records.Where(r => level == null || r.Level == level).Select(r => r.Error == null ? r.Message : $"{r.Message}: {r.Error.Message}"));

    /// <summary>
    /// Enqueues an error log record containing the specified message and an optional exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Error(string message, Exception? error = null) => _records.Enqueue(new LogRecord("error", message, error));

    /// <summary>
    /// Logs a warning message and an optional exception by invoking the warning event and enqueueing a log record.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Warn(string message, Exception? error = null)
    {
        OnWarn?.Invoke(message, error);
        _records.Enqueue(new LogRecord("warn", message, error));
    }

    /// <summary>
    /// Enqueues an informational log record containing the specified message and an optional exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Info(string message, Exception? error = null) => _records.Enqueue(new LogRecord("info", message, error));

    /// <summary>
    /// Enqueues a debug-level log record containing the specified message and an optional exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void Debug(string message, Exception? error = null) => _records.Enqueue(new LogRecord("debug", message, error));

    /// <summary>
    /// Enqueues a trace log record containing the specified direction and frame information.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <param name="frame">The frame.</param>
    public void Trace(string direction, string frame) => _records.Enqueue(new LogRecord("trace", $"{direction} {frame}", null));
}
