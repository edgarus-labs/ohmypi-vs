using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Header, dock and composer text helpers.</summary>
internal static class Chrome
{
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Determines the appropriate StateWord based on the current connection state, session phase, number of pending interactions, and availability status.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="phase">The phase.</param>
    /// <param name="pendingInteractions">The pending interactions.</param>
    /// <param name="unavailable">The unavailable.</param>
    /// <returns>The state word result.</returns>
    public static StateWord GetStateWord(ConnectionState connection, SessionPhase phase, int pendingInteractions, bool unavailable)
    {
        if (unavailable || connection == ConnectionState.Stopped || connection == ConnectionState.Failed)
        {
            return StateWord.Offline;
        }

        if (connection != ConnectionState.Ready)
        {
            return StateWord.Starting;
        }

        if (pendingInteractions > 0)
        {
            return StateWord.Waiting;
        }

        if (phase == SessionPhase.Aborting)
        {
            return StateWord.Aborting;
        }

        return phase == SessionPhase.Idle ? StateWord.Idle : StateWord.Working;
    }

    /// <summary><c>todos 2/5 · current: &lt;task in progress&gt;</c>; null when there are no tasks.</summary>
    public static string? TodoSummary(IReadOnlyList<TodoPhaseView> phases)
    {
        var tasks = phases.SelectMany(phase => phase.Tasks).ToList();
        if (tasks.Count == 0)
        {
            return null;
        }

        var done = tasks.Count(task => task.Status == "completed");
        var current = tasks.FirstOrDefault(task => task.Status == "in_progress");
        var summary = $"todos {done}/{tasks.Count}";

        return current is not null ? $"{summary} · current: {Whitespace.Replace(current.Content, " ").Trim()}" : summary;
    }

    /// <summary><c>ctx 13% · $0.0215</c>, omitting unknown parts.</summary>
    public static string UsageText(ContextUsageView? usage, double? costUsd)
    {
        var parts = new List<string>();
        if (usage is not null)
        {
            parts.Add($"ctx {Math.Round(usage.Percent, MidpointRounding.AwayFromZero):0}%");
        }

        if (costUsd.HasValue)
        {
            parts.Add(Format.FormatCost(costUsd.Value));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Header text for the tier router's <c>setStatus</c> value: <c>router auto</c>, <c>router auto · standard</c>, <c>router off · subagents only</c>, <c>router · classifier down</c>, or a paused hint; other text is returned unchanged.</summary>
    public static string RouterStatusText(string text)
    {
        const string TierPrefix = "tier: ";
        if (StartsWithWord(text, "tier-router: on"))
        {
            return text.EndsWith("(main off)", StringComparison.Ordinal) ? "router off · subagents only" : "router auto";
        }

        if (IsRouterPaused(text))
        {
            return "router paused · manual model, /tier-auto resumes";
        }

        if (StartsWithWord(text, "tier-router: service DOWN"))
        {
            return "router · classifier down";
        }

        if (text.StartsWith(TierPrefix, StringComparison.Ordinal))
        {
            var tier = text.Substring(TierPrefix.Length).Split(' ')[0];

            return tier.Length == 0 ? text : $"router auto · {tier}";
        }

        return text;
    }

    /// <summary>
    /// Determines whether the specified text begins with the given prefix as a distinct word.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="prefix">The prefix.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    private static bool StartsWithWord(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) && (text.Length == prefix.Length || text[prefix.Length] == ' ');

    /// <summary>Whether the tier router's <c>setStatus</c> value says a manual model change paused it, so <c>/tier-auto</c> resumes it.</summary>
    public static bool IsRouterPaused(string text) => StartsWithWord(text, "tier-router: paused");

    /// <summary>Whether the tier router's <c>setStatus</c> value says it is choosing the main model: on with main routing, or a tier verdict.</summary>
    public static bool IsRouterRouting(string text) =>
        text.StartsWith("tier: ", StringComparison.Ordinal) || (StartsWithWord(text, "tier-router: on") && !text.EndsWith("(main off)", StringComparison.Ordinal));

    /// <summary>The composer's model label, marked <c>tier auto</c> while the tier router chooses the model.</summary>
    public static string ComposerModelText(ModelView? model, bool routerAuto)
    {
        if (model is null || model.Name.Length == 0)
        {
            return "Select model";
        }

        return routerAuto ? $"{model.Name} · tier auto" : model.Name;
    }

    /// <summary>The model's name for the header, its id when OMP reports no name, empty without a model.</summary>
    public static string HeaderModelText(ModelView? model) => model is null ? "" : model.Name.Length > 0 ? model.Name : model.Id;

    /// <summary>Elapsed time of a running turn in the shared <see cref="Format.FormatDuration"/> form; empty for the first second.</summary>
    public static string ElapsedText(long ms) => ms < 1000 ? "" : Format.FormatDuration(ms);

    /// <summary><c>1 line</c> / <c>12 lines</c>.</summary>
    public static string LineCount(string text)
    {
        var lines = text.Split('\n').Length;

        return $"{lines} line{(lines == 1 ? "" : "s")}";
    }

    /// <summary>One button: Send when idle, Stop while the agent works.</summary>
    public static ComposerButtonState ComposerButtons(bool busy, bool hasContent, bool unavailable) => new ComposerButtonState
    {
        IsStop = busy,
        Enabled = busy ? !unavailable : hasContent && !unavailable,
    };

    /// <summary>
    /// How a prompt sent from the composer is delivered: while the agent works it steers the running turn (OMP hands
    /// it over between tool calls) unless <paramref name="followUp"/> queues it for after the turn.
    /// </summary>
    public static PromptMode SendMode(bool busy, bool followUp) => busy && followUp ? PromptMode.FollowUp : PromptMode.Auto;
}
