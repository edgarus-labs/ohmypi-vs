namespace Omp.Core.Tests.Support;

public sealed record LogRecord(string Level, string Message, Exception? Error);
