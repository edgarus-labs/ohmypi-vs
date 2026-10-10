namespace Omp.Core.Tests.Support;

/// <summary>
/// Represents a log entry containing the severity level, a descriptive message, and an optional exception.
/// </summary>
/// <param name="Level">The level.</param>
/// <param name="Message">The message.</param>
/// <param name="Error">The error.</param>
public sealed record LogRecord(string Level, string Message, Exception? Error);
