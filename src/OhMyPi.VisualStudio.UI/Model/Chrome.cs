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

    /// <summary>Header text for the tier router's <c>setStatus</c> value: <c>router auto</c>, <c>router auto · standard</c>, or a paused hint; other text is returned unchanged.</summary>
    public static string RouterStatusText(string text)
    {
        const string TierPrefix = "tier: ";
        if (text.StartsWith("tier-router: on", StringComparison.Ordinal))
        {
            return "router auto";
        }

        if (text.StartsWith("tier-router: paused", StringComparison.Ordinal))
        {
            return "router paused · manual model, /tier-auto resumes";
        }

        if (text.StartsWith("tier-router: service DOWN", StringComparison.Ordinal))
        {
            return "router auto · classifier down";
        }

        if (text.StartsWith(TierPrefix, StringComparison.Ordinal))
        {
            var tier = text.Substring(TierPrefix.Length).Split(' ')[0];

            return tier.Length == 0 ? text : $"router auto · {tier}";
        }

        return text;
    }

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
