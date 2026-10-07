using System;
using System.Collections.Generic;
using Omp.Core;
using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("--profile work", new[] { "--profile", "work" })]
    [InlineData("  --a   b  ", new[] { "--a", "b" })]
    [InlineData("--name \"two words\" x", new[] { "--name", "two words", "x" })]
    [InlineData("--arg=\"a b\"c", new[] { "--arg=a bc" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("C:\\dir\\ \"C:\\with space\\\\\"", new[] { "C:\\dir\\", "C:\\with space\\" })]
    [InlineData("\"unterminated value", new[] { "unterminated value" })]
    public void SplitsLikeWindowsCommandLines(string input, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(input));
    }

    [Fact]
    public void NullSplitsToNothing()
    {
        Assert.Empty(CommandLine.Split(null));
    }
}

public class WorkingDirectoryTests
{
    private const string Profile = @"C:\Users\me";

    [Fact]
    public void PrefersTheSolutionDirectory()
    {
        Assert.Equal(@"D:\work\repo", WorkingDirectory.Resolve(@"D:\work\repo\", null, Profile));
    }

    [Fact]
    public void UsesTheOpenFolderWithoutSolution()
    {
        Assert.Equal(@"D:\work\folder", WorkingDirectory.Resolve(null, @"D:\work\folder", Profile));
        Assert.Equal(@"D:\work\folder", WorkingDirectory.Resolve("  ", @"D:\work\folder\", Profile));
    }

    [Fact]
    public void FallsBackToTheUserProfile()
    {
        Assert.Equal(Profile, WorkingDirectory.Resolve(null, null, Profile));
        Assert.Equal(Profile, WorkingDirectory.Resolve("", "", Profile));
    }

    [Fact]
    public void KeepsDriveRoots()
    {
        Assert.Equal(@"D:\", WorkingDirectory.Resolve(@"D:\", null, Profile));
    }

    [Fact]
    public void ComparesDirectoriesIgnoringCaseAndTrailingSeparators()
    {
        Assert.True(WorkingDirectory.Same(@"D:\Work\Repo\", @"d:\work\repo"));
        Assert.False(WorkingDirectory.Same(@"D:\work\repo", @"D:\work\repo2"));
    }
}

public class LastSessionTests
{
    [Fact]
    public void KeyIsStablePerDirectoryIgnoringCaseAndTrailingSeparator()
    {
        var key = LastSession.KeyFor(@"D:\Work\Repo\");
        Assert.Equal(key, LastSession.KeyFor(@"d:\work\repo"));
        Assert.NotEqual(key, LastSession.KeyFor(@"D:\work\repo2"));
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void ResumesThePreferredFileWhileItExists()
    {
        var existing = new HashSet<string> { "a.jsonl", "last.jsonl" };
        var options = LastSession.ResumeOptions("a.jsonl", "last.jsonl", existing.Contains, null);
        Assert.Equal("a.jsonl", options!.ResumeSessionFile);
        Assert.False(options.NewSession);
    }

    [Fact]
    public void ResumesTheLastFileOtherwise()
    {
        var existing = new HashSet<string> { "last.jsonl" };
        Assert.Equal("last.jsonl", LastSession.ResumeOptions("gone.jsonl", "last.jsonl", existing.Contains, null)!.ResumeSessionFile);
    }

    [Fact]
    public void StartsUnboundWhenTheLastFileIsGone()
    {
        var logged = new List<string>();
        Assert.Null(LastSession.ResumeOptions(null, "gone.jsonl", _ => false, logged.Add));
        Assert.Null(LastSession.ResumeOptions(null, null, _ => true, logged.Add));
        Assert.Equal(new[] { "Last session file no longer exists: gone.jsonl" }, logged);
    }
}

public class LogFormatTests
{
    [Theory]
    [InlineData("Info", "Error", true)]
    [InlineData("Info", "Warn", true)]
    [InlineData("Info", "Info", true)]
    [InlineData("Info", "Debug", false)]
    [InlineData("Error", "Warn", false)]
    [InlineData("Debug", "Debug", true)]
    public void FiltersByConfiguredLevel(string configured, string level, bool expected)
    {
        Assert.Equal(expected, LogFormat.Allows((OmpLogLevel)Enum.Parse(typeof(OmpLogLevel), configured), (OmpLogLevel)Enum.Parse(typeof(OmpLogLevel), level)));
    }

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
