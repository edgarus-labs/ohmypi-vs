using System;
using System.Globalization;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>
/// Provides a set of utility methods for formatting log entries, including timestamps, trace identifiers, line numbers, and permission levels.
/// </summary>
internal static class LogFormat
{
    /// <summary>
    /// Determines whether a specific log level is permitted based on the configured minimum log level.
    /// </summary>
    /// <param name="configured">The configured.</param>
    /// <param name="level">The level.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public static bool Allows(OmpLogLevel configured, OmpLogLevel level) => level <= configured;

    /// <summary>
    /// Formats a log entry as a single string containing a timestamp, the log level, a message, and optional exception details.
    /// </summary>
    /// <param name="utc">The utc.</param>
    /// <param name="level">The level.</param>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    /// <returns>The string result.</returns>
    public static string Line(DateTime utc, OmpLogLevel level, string message, Exception? error)
    {
        var text = error is null ? message : $"{message}: {error}";

        return $"{Timestamp(utc)} [{level.ToString().ToLowerInvariant()}] {text}";
    }

    /// <summary>
    /// Formats a trace log entry containing a timestamp, a directional indicator, and the specified frame information.
    /// </summary>
    /// <param name="utc">The utc.</param>
    /// <param name="direction">The direction.</param>
    /// <param name="frame">The frame.</param>
    /// <returns>The string result.</returns>
    public static string Trace(DateTime utc, string direction, string frame) =>
        $"{Timestamp(utc)} [trace] {(direction == "in" ? "<-" : "->")} {frame}";

    /// <summary>
    /// Formats a UTC date and time into a standardized ISO 8601 string representation with millisecond precision.
    /// </summary>
    /// <param name="utc">The utc.</param>
    /// <returns>The string result.</returns>
    private static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
