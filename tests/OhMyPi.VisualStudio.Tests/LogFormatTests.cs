using OhMyPi.VisualStudio.Logic;
using System;

namespace OhMyPi.VisualStudio.Tests;

public sealed class LogFormatTests
{
    [Theory]
    [InlineData("Info", "Error", true)]
    [InlineData("Info", "Warn", true)]
    [InlineData("Info", "Info", true)]
    [InlineData("Info", "Debug", false)]
    [InlineData("Error", "Warn", false)]
    [InlineData("Debug", "Debug", true)]
    public void FiltersByConfiguredLevel(string configured, string level, bool expected) => Assert.Equal(expected, LogFormat.Allows((OmpLogLevel)Enum.Parse(typeof(OmpLogLevel), configured), (OmpLogLevel)Enum.Parse(typeof(OmpLogLevel), level)));

    [Fact]
    public void FormatsLinesWithIsoTimestampAndLevel()
    {
        var at = new DateTime(2026, 10, 4, 9, 8, 7, 654, DateTimeKind.Utc);
        Assert.Equal("2026-10-04T09:08:07.654Z [warn] careful", LogFormat.Line(at, OmpLogLevel.Warn, "careful", null));
        Assert.StartsWith("2026-10-04T09:08:07.654Z [error] boom: System.InvalidOperationException: bad", LogFormat.Line(at, OmpLogLevel.Error, "boom", new InvalidOperationException("bad")));
    }

    [Fact]
    public void FormatsTraceFramesWithArrows()
    {
        var at = new DateTime(2026, 10, 4, 9, 8, 7, 0, DateTimeKind.Utc);
        Assert.Equal("2026-10-04T09:08:07.000Z [trace] <- {\"a\":1}", LogFormat.Trace(at, "in", "{\"a\":1}"));
        Assert.Equal("2026-10-04T09:08:07.000Z [trace] -> {}", LogFormat.Trace(at, "out", "{}"));
    }
}
