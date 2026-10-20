using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

public sealed class UsageBucketsTests
{
    private static readonly string Text = string.Join("\n",
        "```",
        "Usage (0s ago)",
        "",
        "Google Antigravity",
        "- Gemini — Weekly",
        "  user@example.com: 9.64% used (90.4% left)",
        "  resets in 7d",
        "- Gemini — Weekly",
        "  user@example.com: 2.77% used (97.2% left)",
        "  resets in 6d",
        "- Gemini — 5 Hour",
        "  user@example.com: 0.00% used (100.0% left)",
        "  resets in 5h",
        "```");

    private const string Json = """
        {"reports":[{"provider":"google-antigravity","limits":[
          {"id":"google-antigravity:google:default:gemini-argon-wtu-weekly","label":"Gemini","scope":{"windowId":"weekly"},"window":{"id":"weekly","label":"Weekly"},"amount":{"unit":"percent","usedFraction":0.09636587}},
          {"id":"google-antigravity:google:default:gemini-weekly","label":"Gemini","scope":{"windowId":"weekly"},"window":{"id":"weekly","label":"Weekly"},"amount":{"unit":"percent","usedFraction":0.02770454}},
          {"id":"google-antigravity:google:default:gemini-5h","label":"Gemini","scope":{"windowId":"5h"},"window":{"id":"5h","label":"5 Hour"},"amount":{"unit":"percent","usedFraction":0}}
        ]}]}
        """;

    private static IEnumerable<string> Names(IReadOnlyList<ProviderUsage> providers) => providers[0].Limits.Select(limit => limit.Name);

    [Fact]
    public void Names_the_bucket_a_limit_belongs_to_when_the_report_ids_one()
    {
        var providers = UsageReport.Parse(Text);

        UsageBuckets.Qualify(providers, Json);

        Assert.Equal(new[] { "Gemini Argon — Weekly", "Gemini — Weekly", "Gemini — 5 Hour" }, Names(providers));
    }

    [Fact]
    public void Names_a_bucket_that_has_no_twin()
    {
        var providers = UsageReport.Parse("```\nUsage (0s ago)\n\nGoogle Antigravity\n- Gemini — Weekly\n  user@example.com: 9.64% used (90.4% left)\n```");

        UsageBuckets.Qualify(providers, Json);

        Assert.Equal(new[] { "Gemini Argon — Weekly" }, Names(providers));
    }

    [Fact]
    public void Pairs_each_limit_by_its_used_share_when_the_reports_list_them_in_another_order()
    {
        var providers = UsageReport.Parse(Text);
        var swapped = Json.Replace("gemini-argon-wtu-weekly", "TMP").Replace("gemini-weekly", "gemini-argon-wtu-weekly").Replace("TMP", "gemini-weekly");

        UsageBuckets.Qualify(providers, swapped);

        Assert.Equal(new[] { "Gemini — Weekly", "Gemini Argon — Weekly" }, Names(providers).Take(2));
    }

    [Fact]
    public void Leaves_a_limit_alone_when_no_reported_limit_has_its_used_share()
    {
        var providers = UsageReport.Parse(Text);
        var stale = Json.Replace("0.09636587", "0.5");

        UsageBuckets.Qualify(providers, stale);

        Assert.Equal(new[] { "Gemini — Weekly", "Gemini — Weekly" }, Names(providers).Take(2));
    }

    [Fact]
    public void Does_not_turn_the_ids_of_other_providers_into_names()
    {
        var providers = UsageReport.Parse(string.Join("\n",
            "```",
            "Usage (0s ago)",
            "",
            "Cursor",
            "- Other Models — Monthly",
            "  user@example.com: 0.00 usd used (100.0% left)",
            "",
            "Anthropic",
            "- Claude 7 Day (Fable)",
            "  user@example.com: 0.00% used (100.0% left)",
            "```"));
        const string json = """
            {"reports":[
              {"provider":"cursor","limits":[{"id":"cursor:usd:individual-api","label":"Other Models","scope":{"windowId":"monthly"},"window":{"id":"monthly","label":"Monthly"},"amount":{"unit":"usd","usedFraction":0}}]},
              {"provider":"anthropic","limits":[{"id":"anthropic:7d:fable","label":"Claude 7 Day (Fable)","scope":{"windowId":"7d","tier":"fable"},"window":{"id":"7d","label":"7 Day"},"amount":{"unit":"percent","usedFraction":0}}]}
            ]}
            """;

        UsageBuckets.Qualify(providers, json);

        Assert.Equal("Other Models — Monthly", providers[0].Limits[0].Name);
        Assert.Equal("Claude 7 Day (Fable)", providers[1].Limits[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"reports\":[{\"provider\":\"google-antigravity\"}]}")]
    public void Leaves_every_name_alone_when_the_report_is_not_usable(string json)
    {
        var providers = UsageReport.Parse(Text);

        UsageBuckets.Qualify(providers, json);

        Assert.Equal(new[] { "Gemini — Weekly", "Gemini — Weekly", "Gemini — 5 Hour" }, Names(providers));
    }
}
