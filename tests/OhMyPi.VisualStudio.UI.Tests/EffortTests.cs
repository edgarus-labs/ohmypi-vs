using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class EffortTests
{
    private static SessionView Session(string? selector, string? level, string? resolved, params string[] levels) =>
        new SessionView { ThinkingSelector = selector, ThinkingLevel = level, ThinkingResolved = resolved, AvailableThinkingLevels = levels };

    [Fact]
    public void Auto_shows_the_resolved_effort()
    {
        var session = Session("auto", "low", "low", "off", "auto", "low", "high");
        var choices = Effort.Choices(session);
        Assert.Equal("auto", choices.Value);
        Assert.Equal(new[] { "off", "auto · low", "low", "high" }, choices.Options.Select(o => o.Label));
        Assert.Equal(new[] { "off", "auto", "low", "high" }, choices.Options.Select(o => o.Value));
        Assert.Equal("auto · low", Effort.Label(session));
        Assert.Equal("Reasoning effort: auto (OMP picks it per turn, now low)", Effort.Tooltip(session));
    }

    [Fact]
    public void Auto_before_resolution_is_plain_auto()
    {
        var session = Session("auto", null, null, "off", "auto", "low");
        Assert.Equal("auto", Effort.Label(session));
        Assert.Equal("Reasoning effort: auto (OMP picks it per turn)", Effort.Tooltip(session));
    }

    [Fact]
    public void Explicit_level_replaces_auto()
    {
        var session = Session("high", "high", "low", "off", "auto", "low", "high");
        Assert.Equal("high", Effort.Label(session));
        Assert.Equal("auto", Effort.Choices(session).Options[1].Label);
        Assert.Equal("Reasoning effort", Effort.Tooltip(session));
    }

    [Fact]
    public void Falls_back_to_effective_level_without_selector()
    {
        var session = Session(null, "medium", null, "off", "medium");
        Assert.Equal("medium", Effort.Choices(session).Value);
        Assert.Equal("medium", Effort.Label(session));
    }

    [Fact]
    public void No_levels_means_no_selector()
    {
        var session = Session(null, null, null);
        Assert.Empty(Effort.Choices(session).Options);
        Assert.Equal("", Effort.Label(session));
    }
}
