using System;
using System.Globalization;

namespace OhMyPi.VisualStudio.Logic;

internal static class LogFormat
{
    public static bool Allows(OmpLogLevel configured, OmpLogLevel level) => level <= configured;

    public static string Line(DateTime utc, OmpLogLevel level, string message, Exception? error)
    {
        var text = error is null ? message : $"{message}: {error}";

        return $"{Timestamp(utc)} [{level.ToString().ToLowerInvariant()}] {text}";
    }

    public static string Trace(DateTime utc, string direction, string frame) =>
        $"{Timestamp(utc)} [trace] {(direction == "in" ? "<-" : "->")} {frame}";

    private static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
