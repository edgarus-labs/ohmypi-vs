using System.Collections.Concurrent;

namespace Omp.Core.Tests.Support;

/// <summary>Records every message; an error argument is appended as <c>: message</c>.</summary>
public sealed class MemoryLogger : IOmpLogger
{
    private readonly ConcurrentQueue<LogRecord> _records = new();

    public MemoryLogger(bool trace = false)
    {
        TraceEnabled = trace;
    }

    public bool TraceEnabled { get; set; }

    public IReadOnlyList<LogRecord> Records => _records.ToArray();

    public Action<string, Exception?>? OnWarn { get; set; }

    public string Text(string? level = null) =>
        string.Join("\n", _records.Where(r => level == null || r.Level == level).Select(r => r.Error == null ? r.Message : $"{r.Message}: {r.Error.Message}"));

    public void Error(string message, Exception? error = null) => _records.Enqueue(new LogRecord("error", message, error));

    public void Warn(string message, Exception? error = null)
    {
        OnWarn?.Invoke(message, error);
        _records.Enqueue(new LogRecord("warn", message, error));
    }

    public void Info(string message, Exception? error = null) => _records.Enqueue(new LogRecord("info", message, error));

    public void Debug(string message, Exception? error = null) => _records.Enqueue(new LogRecord("debug", message, error));

    public void Trace(string direction, string frame) => _records.Enqueue(new LogRecord("trace", $"{direction} {frame}", null));
}
