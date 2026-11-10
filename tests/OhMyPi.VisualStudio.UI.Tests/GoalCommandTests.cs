using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class GoalCommandTests
{
    [Theory]
    [InlineData("/goal Ship the release", GoalOperation.Create, "Ship the release")]
    [InlineData("  /GOAL set Ship the release  ", GoalOperation.Create, "Ship the release")]
    [InlineData("/goal Fix\nthe build", GoalOperation.Create, "Fix\nthe build")]
    [InlineData("/goal", GoalOperation.Get, null)]
    [InlineData("/goal show", GoalOperation.Get, null)]
    [InlineData("/goal pause", GoalOperation.Pause, null)]
    [InlineData("/goal Resume", GoalOperation.Resume, null)]
    [InlineData("/goal drop", GoalOperation.Drop, null)]
    public void Parses_what_the_user_asks_of_the_goal(string text, GoalOperation operation, string? objective)
    {
        var request = GoalCommand.Parse(text)!;
        Assert.Equal((operation, objective, null), (request.Operation, request.Objective, request.Error));
    }

    [Theory]
    [InlineData("/goals")]
    [InlineData("/goalie keep")]
    [InlineData("set a goal")]
    [InlineData("/compact")]
    public void Other_prompts_are_not_goal_commands(string text) => Assert.Null(GoalCommand.Parse(text));

    [Theory]
    [InlineData("/goal set")]
    [InlineData("/goal budget 5000")]
    [InlineData("/goal pause now")]
    [InlineData("/goal resume later")]
    [InlineData("/goal drop it")]
    [InlineData("/goal show all")]
    public void A_goal_command_omp_cannot_run_is_an_error(string text) => Assert.NotNull(GoalCommand.Parse(text)!.Error);

    [Fact]
    public void Goal_is_offered_after_omps_commands_unless_omp_lists_it()
    {
        var omp = new[] { new SlashCommandView { Name = "model" }, new SlashCommandView { Name = "compact" } };
        Assert.Equal(new[] { "model", "compact", "goal" }, GoalCommand.WithGoal(omp).Select(c => c.Name));

        var listed = new[] { new SlashCommandView { Name = "goal" }, new SlashCommandView { Name = "model" } };
        Assert.Same(listed, GoalCommand.WithGoal(listed));
    }

    [Fact]
    public void Describes_the_goal_omp_reports()
    {
        var goal = new GoalView { Objective = "Ship it", Status = "active", TokensUsed = 120, TokenBudget = 5000 };
        Assert.Equal("Goal set: Ship it", GoalCommand.Describe(GoalOperation.Create, goal));
        Assert.Equal("Goal paused: Ship it", GoalCommand.Describe(GoalOperation.Pause, goal));
        Assert.Equal("Goal resumed: Ship it", GoalCommand.Describe(GoalOperation.Resume, goal));
        Assert.Equal("Goal dropped.", GoalCommand.Describe(GoalOperation.Drop, null));
        Assert.Equal("Goal (active, 120 of 5000 tokens): Ship it", GoalCommand.Describe(GoalOperation.Get, goal));
        Assert.Equal("Goal (paused, 120 tokens): Ship it", GoalCommand.Describe(GoalOperation.Get, new GoalView { Objective = "Ship it", Status = "paused", TokensUsed = 120 }));
        Assert.Equal("No goal is set.", GoalCommand.Describe(GoalOperation.Get, null));
    }
}
