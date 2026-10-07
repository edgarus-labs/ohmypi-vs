using Newtonsoft.Json.Linq;

namespace Omp.Core.Tests;

public class FormatTests
{
    [Fact]
    public void SummarizesATurnWithItsScopeDurationTokensAndCost()
    {
        var turn = new TurnSummaryItem { DurationMs = 72_400, InputTokens = 1200, CacheReadTokens = 30_000, CacheWriteTokens = 800, OutputTokens = 350, CostUsd = 0.0412 };
        Assert.Equal("Whole turn · 1m 12s · 32k in · 350 out · $0.0412", Format.TurnSummary(turn));
        Assert.Equal("Whole turn · aborted after 850ms · 10 in · 5 out", Format.TurnSummary(new TurnSummaryItem { DurationMs = 850, InputTokens = 10, OutputTokens = 5, Aborted = true }));
    }

    [Fact]
    public void TitlesASessionByTitleFirstMessageOrFileNameWithoutExtension()
    {
        const string file = "C:\\s\\2026-10-04_abc.jsonl";
        Assert.Equal("Fix login", Format.SessionTitle(new SessionSummary { Title = "  Fix\n login  ", FirstMessage = "ignored", Path = file }));
        Assert.Equal("hello world", Format.SessionTitle(new SessionSummary { FirstMessage = "hello\n\nworld", Path = file }));
        Assert.Equal(new string('x', 150), Format.SessionTitle(new SessionSummary { Title = " ", FirstMessage = new string('x', 150), Path = file }));
        Assert.Equal("2026-10-04_abc", Format.SessionTitle(new SessionSummary { Title = "", FirstMessage = "", Path = file }));
    }

    [Fact]
    public void FormatsBytes()
    {
        Assert.Equal("512 B", Format.FormatBytes(512));
        Assert.Equal("2.0 KB", Format.FormatBytes(2048));
        Assert.Equal("5.0 MB", Format.FormatBytes(5 * 1024 * 1024));
    }

    [Fact]
    public void CompactsTokens()
    {
        Assert.Equal("999", Format.CompactTokens(999));
        Assert.Equal("200k", Format.CompactTokens(200_000));
        Assert.Equal("1.5k", Format.CompactTokens(1_500));
        Assert.Equal("1M", Format.CompactTokens(1_000_000));
        Assert.Equal("1.2M", Format.CompactTokens(1_234_567));
        Assert.Equal("1M", Format.CompactTokens(999_960));
    }

    [Fact]
    public void RendersAnAgentSummary()
    {
        var agent = new AgentView
        {
            Id = "a1",
            ParentId = "main",
            Name = "scout",
            Model = "m",
            Status = AgentStatus.Failed,
            Activity = "read",
            Tokens = 1200,
            CostUsd = 0.0123,
            StartedAt = 1000,
            EndedAt = 4000,
            Error = "boom",
        };
        var text = Format.AgentSummary(agent, 5000);
        Assert.StartsWith("scout\n", text);
        foreach (var part in new[] { "id: a1", "parent: main", "status: failed", "model: m", "activity: read", "tokens: 1,200", "cost: $0.0123", "duration: 3.0s", "error: boom" })
            Assert.Contains(part, text);
        Assert.DoesNotContain("tools:", text);
    }

    [Fact]
    public void AgentSummaryMeasuresARunningAgentUntilNow()
    {
        var text = Format.AgentSummary(new AgentView { Id = "main", Name = "main", Status = AgentStatus.Running, StartedAt = 0 }, 125_000);
        Assert.Contains("duration: 2m 5s", text);
        Assert.Contains("status: running", text);
    }

    [Fact]
    public void RendersATranscriptAsMarkdown()
    {
        var items = new TranscriptItem[]
        {
            new UserItem { Id = "u", Text = "hello" },
            new AssistantItem { Id = "a", Text = "hi", Thinking = "hmm" },
            new ToolItem { Id = "t", Name = "read", Args = new JObject { ["path"] = "a.ts" }, Status = ToolStatus.Done, StartedAt = 0, EndedAt = 10, Result = new ToolResultView { Text = "body" } },
            new NoticeItem { Id = "n", Level = NoticeLevel.Error, Text = "bad" },
            new CommandOutputItem { Id = "c", Text = "$ ls\nx" },
        };
        var md = Format.TranscriptToMarkdown("Agent main", items);
        Assert.StartsWith("# Agent main", md);
        Assert.Contains("## User\n\nhello", md);
        Assert.Contains("## Assistant\n\n> hmm\n\nhi", md);
        Assert.Contains("### Tool `read` — done", md);
        Assert.Contains("```\n{\n  \"path\": \"a.ts\"\n}\n```", md);
        Assert.Contains("```\nbody\n```", md);
        Assert.Contains("**error:** bad", md);
        Assert.Contains("```\n$ ls\nx\n```", md);
        Assert.EndsWith("\n", md);
    }

    [Fact]
    public void FencesTextWithMoreBackticksThanItContains()
    {
        Assert.Equal("```", Format.FenceFor("plain"));
        Assert.Equal("`````", Format.FenceFor("a ```` b"));
    }

    [Fact]
    public void FormatsCostsWithMorePrecisionBelowADollar()
    {
        Assert.Equal("$0.0215", Format.FormatCost(0.02149));
        Assert.Equal("$3.46", Format.FormatCost(3.456));
        Assert.Equal("$1.00", Format.FormatCost(0.99996));
    }

    [Fact]
    public void FormatsDurations()
    {
        Assert.Equal("850ms", Format.FormatDuration(850));
        Assert.Equal("12.3s", Format.FormatDuration(12_300));
        Assert.Equal("2m 5s", Format.FormatDuration(125_000));
        Assert.Equal("1m 0s", Format.FormatDuration(59_960));
    }

    [Fact]
    public void FormatsCompactRelativeTimesWithUnitBoundaries()
    {
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        const long s = 1000, m = 60 * s, hr = 60 * m, d = 24 * hr;
        string Ago(long ms) => Format.RelativeTime(now - ms, now);
        Assert.Equal("now", Ago(0));
        Assert.Equal("now", Ago(-5 * m));
        Assert.Equal("now", Ago(59 * s));
        Assert.Equal("1m", Ago(60 * s));
        Assert.Equal("5m", Ago(5 * m + 30 * s));
        Assert.Equal("59m", Ago(59 * m + 59 * s));
        Assert.Equal("1h", Ago(60 * m));
        Assert.Equal("23h", Ago(23 * hr + 59 * m));
        Assert.Equal("1d", Ago(24 * hr));
        Assert.Equal("6d", Ago(6 * d + 23 * hr));
        Assert.Equal("1w", Ago(7 * d));
        Assert.Equal("4w", Ago(29 * d));
        Assert.Equal("1mo", Ago(30 * d));
        Assert.Equal("4mo", Ago(125 * d));
        Assert.Equal("11mo", Ago(364 * d));
        Assert.Equal("1y", Ago(365 * d));
        Assert.Equal("3y", Ago(3 * 365 * d + 10 * d));
    }
}
