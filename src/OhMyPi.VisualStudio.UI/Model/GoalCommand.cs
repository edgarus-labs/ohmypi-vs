using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What a <c>/goal</c> prompt asks of the session's goal; <see cref="Error"/> when OMP cannot run it.</summary>
internal sealed class GoalRequest
{
    public GoalOperation Operation { get; set; }

    public string? Objective { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// <c>/goal</c>, typed in the composer. OMP's command catalog does not list it, since OMP runs it as its own <c>goal</c>
/// RPC command, so the composer offers it and sends it there instead of as a prompt.
/// </summary>
internal static class GoalCommand
{
    private const string Name = "goal";

    private static readonly SlashCommandView Command = new()
    {
        Name = Name,
        Description = "Set a persistent objective OMP works on until it is done; show, pause, resume or drop it",
        Hint = "[objective|show|pause|resume|drop]",
        Source = "builtin",
    };

    /// <summary><paramref name="commands"/> with <c>/goal</c> after them, unless OMP already lists it.</summary>
    public static IReadOnlyList<SlashCommandView> WithGoal(IReadOnlyList<SlashCommandView> commands) =>
        commands.Any(c => string.Equals(c.Name, Name, StringComparison.OrdinalIgnoreCase)) ? commands : [.. commands, Command];

    /// <summary>
    /// The request a <c>/goal</c> prompt makes, or null for any other prompt. With no argument or <c>show</c> it reads the
    /// goal; <c>pause</c>, <c>resume</c> and <c>drop</c> act on it; any other text, optionally after <c>set</c>, is the objective.
    /// A subcommand followed by more text is an error rather than an objective, so a mistyped command never starts work.
    /// </summary>
    public static GoalRequest? Parse(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/" + Name, StringComparison.OrdinalIgnoreCase)
            || (trimmed.Length > Name.Length + 1 && !char.IsWhiteSpace(trimmed[Name.Length + 1])))
        {
            return null;
        }

        var argument = trimmed.Substring(Name.Length + 1).Trim();
        var space = argument.IndexOfAny([' ', '\t', '\r', '\n']);
        var word = (space < 0 ? argument : argument.Substring(0, space)).ToLowerInvariant();
        var rest = space < 0 ? "" : argument.Substring(space + 1).Trim();
        return word switch
        {
            "" => new GoalRequest { Operation = GoalOperation.Get },
            "show" => Bare(GoalOperation.Get, word, rest),
            "pause" => Bare(GoalOperation.Pause, word, rest),
            "resume" => Bare(GoalOperation.Resume, word, rest),
            "drop" => Bare(GoalOperation.Drop, word, rest),
            "set" when rest.Length == 0 => new GoalRequest { Operation = GoalOperation.Create, Error = "/goal set needs an objective, for example: /goal set Make the build green" },
            "set" => new GoalRequest { Operation = GoalOperation.Create, Objective = rest },
            "budget" => new GoalRequest { Operation = GoalOperation.Get, Error = "OMP does not let Visual Studio change the goal's token budget" },
            _ => new GoalRequest { Operation = GoalOperation.Create, Objective = argument },
        };
    }

    /// <summary>A subcommand that takes no argument; an error when <paramref name="rest"/> is not empty.</summary>
    private static GoalRequest Bare(GoalOperation operation, string word, string rest) => new()
    {
        Operation = operation,
        Error = rest.Length == 0 ? null : $"/goal {word} takes no argument; to set a new objective use /goal set <objective>",
    };

    /// <summary>The notice shown after OMP ran <paramref name="operation"/>; <paramref name="goal"/> is the goal it reported.</summary>
    public static string Describe(GoalOperation operation, GoalView? goal) => operation switch
    {
        GoalOperation.Drop => "Goal dropped.",
        _ when goal is null => "No goal is set.",
        GoalOperation.Create => $"Goal set: {goal.Objective}",
        GoalOperation.Pause => $"Goal paused: {goal.Objective}",
        GoalOperation.Resume => $"Goal resumed: {goal.Objective}",
        _ => $"Goal ({goal.Status}, {goal.TokensUsed}{(goal.TokenBudget is long budget ? $" of {budget}" : "")} tokens): {goal.Objective}",
    };
}
