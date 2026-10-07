using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

public sealed class UsageReportTests
{
    private const string Esc = "\u001b";

    /// <summary>The shape of OMP's <c>/usage</c> output, with made-up accounts.</summary>
    private static readonly string Output = string.Join("\n",
        "```",
        "Usage (0s ago)",
        "",
        "Anthropic",
        "  Models with usage data",
        "    anthropic/claude-opus-5-5",
        "    anthropic/claude-sonnet-5-5",
        "- Claude 5 Hour",
        "  user@example.com (Example Org): 4.00% used (96.0% left)  ← in use by this session",
        $"  [{Esc}[38;2;107;114;128m█░░░░░░░░░░░░░░░░░░░░░░░{Esc}[39m] 4%",
        "  resets in 4h",
        "- Claude 7 Day",
        "  user@example.com (Example Org): 32.00% used (68.0% left)  ← in use by this session",
        "  [████████░░░░░░░░░░░░░░░░] 32%",
        "  resets in 5d",
        "",
        "Cursor",
        "  Models with usage data",
        "    cursor/default",
        "- Other Models — Monthly",
        "  user@example.com: 20.00 usd used (0.0% left)",
        "  [████████████████████████] 100%",
        "  resets in 40h",
        "",
        "Openai Codex",
        "  Models with usage data",
        "    openai-codex/gpt-6-astra",
        "- user@example.com (team-id) · plan: team: 1 saved rate-limit reset — available — /usage reset to spend",
        "  soonest expires in 23d (2026-10-29)",
        "- 5 hours",
        "  user@example.com (team-id) · plan: team: 100.00% used (0.0% left)",
        "  [████████████████████████] 100%",
        "  resets in 2h",
        "```");

    [Fact]
    public void Reads_each_provider_with_its_limits_in_order()
    {
        var report = UsageReport.Parse(Output);

        Assert.Equal(new[] { "Anthropic", "Cursor", "Openai Codex" }, report.Select(p => p.Provider));
        Assert.Equal(new[] { "Claude 5 Hour", "Claude 7 Day" }, report[0].Limits.Select(l => l.Name));
        Assert.Equal(new[] { "Other Models — Monthly" }, report[1].Limits.Select(l => l.Name));
        Assert.Equal(new[] { "5 hours" }, report[2].Limits.Select(l => l.Name));
    }

    [Fact]
    public void A_limit_carries_its_account_used_share_reset_and_whether_this_session_uses_it()
    {
        var limit = UsageReport.Parse(Output)[0].Limits[1];

        Assert.Equal("user@example.com (Example Org)", limit.Account);
        Assert.Equal(32.0, limit.UsedPercent);
        Assert.Equal("in 5d", limit.Resets);
        Assert.True(limit.InUse);
    }

    [Fact]
    public void A_limit_counted_in_money_takes_its_used_share_from_what_is_left()
    {
        var limit = UsageReport.Parse(Output)[1].Limits[0];

        Assert.Equal(100.0, limit.UsedPercent);
        Assert.False(limit.InUse);
    }

    [Fact]
    public void Entries_without_a_used_share_are_left_out()
    {
        var codex = UsageReport.Parse(Output)[2];

        Assert.Single(codex.Limits);
        Assert.Equal(100.0, codex.Limits[0].UsedPercent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown command: usage")]
    public void Text_that_is_not_a_usage_report_reads_as_no_providers(string text) => Assert.Empty(UsageReport.Parse(text));
}
