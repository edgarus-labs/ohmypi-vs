using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class SlashCommandsTests
{
    private static readonly SlashCommandView[] Commands =
    [
        new SlashCommandView { Name = "model", Aliases = new[] { "models" } },
        new SlashCommandView { Name = "compact" },
        new SlashCommandView { Name = "skill:react-best-practices" },
        new SlashCommandView { Name = "context" },
    ];

    [Theory]
    [InlineData("/", "")]
    [InlineData("/mo", "mo")]
    [InlineData("/MO", "MO")]
    [InlineData("/model gpt", null)]
    [InlineData("/model\n", null)]
    [InlineData("fix /model", null)]
    [InlineData("", null)]
    public void Query_is_the_command_word_while_it_is_still_being_typed(string text, string? expected) => Assert.Equal(expected, SlashCommands.Query(text));

    [Fact]
    public void Matches_prefix_on_name_or_alias_before_substring_matches()
    {
        Assert.Equal(new[] { "compact", "context" }, SlashCommands.Match(Commands, "co").Select(c => c.Name));
        Assert.Equal(new[] { "model" }, SlashCommands.Match(Commands, "models").Select(c => c.Name));
        Assert.Equal(new[] { "skill:react-best-practices" }, SlashCommands.Match(Commands, "react").Select(c => c.Name));
        Assert.Equal(new[] { "model", "compact", "context" }, SlashCommands.Match(Commands, "o").Select(c => c.Name).Take(3));
    }

    [Fact]
    public void Empty_query_lists_every_command_in_omp_order() => Assert.Equal(Commands.Select(c => c.Name), SlashCommands.Match(Commands, "").Select(c => c.Name));

    [Fact]
    public void An_exactly_typed_name_or_alias_is_matched_first()
    {
        var commands = new[]
        {
            new SlashCommandView { Name = "new-session" },
            new SlashCommandView { Name = "newer" },
            new SlashCommandView { Name = "new" },
            new SlashCommandView { Name = "clear", Aliases = new[] { "n" } },
        };
        Assert.Equal(new[] { "new", "new-session", "newer" }, SlashCommands.Match(commands, "new").Select(c => c.Name));
        Assert.Equal("clear", SlashCommands.Match(commands, "N").First().Name);
    }

    [Theory]
    [InlineData("/compact", true)]
    [InlineData("  /compact now", true)]
    [InlineData("compact", false)]
    [InlineData("// comment", true)]
    public void Detects_slash_command_prompts(string text, bool expected) => Assert.Equal(expected, SlashCommands.IsCommand(text));

    [Fact]
    public void A_slash_command_is_sent_trimmed_so_omp_sees_the_leading_slash()
    {
        var prompt = Attachments.Compose("  /session ", new Attachment[0], "C:\\repo");
        Assert.Equal("/session", prompt.Message);
    }
}
