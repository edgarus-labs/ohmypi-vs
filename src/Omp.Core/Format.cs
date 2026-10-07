using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Omp.Core;

/// <summary>Display formatting shared by the tool window views.</summary>
public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.CultureInvariant);
    private static readonly Regex Backticks = new Regex("`+", RegexOptions.CultureInvariant);

    /// <summary>Display title of a saved session: its title, else its first message, else its file name; whitespace collapsed.</summary>
    public static string SessionTitle(SessionSummary session)
    {
        var title = Collapse(session.Title);
        if (!string.IsNullOrEmpty(title))
        {
            return title!;
        }

        var firstMessage = Collapse(session.FirstMessage);
        if (!string.IsNullOrEmpty(firstMessage))
        {
            return firstMessage!;
        }

        var name = System.IO.Path.GetFileName(session.Path);

        return name.EndsWith(".jsonl", StringComparison.Ordinal) && name.Length > ".jsonl".Length ? name.Substring(0, name.Length - ".jsonl".Length) : name;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024d).ToString("F1", Invariant) + " KB";
        }

        return (bytes / (1024d * 1024d)).ToString("F1", Invariant) + " MB";
    }

    /// <summary><c>999</c>, <c>1.5k</c>, <c>200k</c>, <c>1.2M</c>; the unit is chosen after rounding, so 999 960 is <c>1M</c>.</summary>
    public static string CompactTokens(long n)
    {
        if (n >= 1_000_000 || Math.Round(n / 1000d, 1, MidpointRounding.AwayFromZero) >= 1000)
        {
            return Trimmed(n / 1_000_000d, 1) + "M";
        }

        if (n >= 1000)
        {
            return Trimmed(n / 1000d, 1) + "k";
        }

        return n.ToString(Invariant);
    }

    /// <summary><c>Whole turn · 1m 12s · 32k in · 350 out · $0.0412</c>; "in" counts cached prompt tokens too.</summary>
    public static string TurnSummary(TurnSummaryItem turn)
    {
        var duration = FormatDuration(turn.DurationMs);
        var parts = new List<string>
        {
            "Whole turn",
            turn.Aborted ? "aborted after " + duration : duration,
            CompactTokens(turn.InputTokens + turn.CacheReadTokens + turn.CacheWriteTokens) + " in",
            CompactTokens(turn.OutputTokens) + " out",
        };
        if (turn.CostUsd is not null)
        {
            parts.Add(FormatCost(turn.CostUsd.Value));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Plain-text summary: the agent name, then one <c>field: value</c> line per known field.</summary>
    public static string AgentSummary(AgentView agent, long nowMs)
    {
        var rows = new (string Field, string? Value)[]
        {
            ("id", agent.Id),
            ("parent", agent.ParentId),
            ("status", StatusName(agent.Status)),
            ("model", agent.Model),
            ("description", agent.Description),
            ("activity", agent.Activity),
            ("tools", agent.ToolCount?.ToString(Invariant)),
            ("tokens", agent.Tokens?.ToString("N0", Invariant)),
            ("cost", agent.CostUsd.HasValue ? FormatCost(agent.CostUsd.Value) : null),
            ("duration", agent.StartedAt.HasValue ? FormatDuration((agent.EndedAt ?? nowMs) - agent.StartedAt.Value) : null),
            ("session file", agent.SessionFile),
            ("error", agent.Error),
        };
        var lines = new List<string> { agent.Name };
        foreach (var (field, value) in rows)
        {
            if (!string.IsNullOrEmpty(value))
            {
                lines.Add($"{field}: {value!.Replace('\n', ' ')}");
            }
        }

        return string.Join("\n", lines) + "\n";
    }

    public static string TranscriptToMarkdown(string title, IReadOnlyList<TranscriptItem> items)
    {
        var blocks = new List<string> { "# " + title };
        foreach (var item in items)
        {
            switch (item)
            {
                case UserItem user:
                    blocks.Add("## User\n\n" + user.Text);
                    break;

                case AssistantItem assistant:
                    {
                        var parts = new List<string> { "## Assistant" + (assistant.Model is not null ? $" ({assistant.Model})" : "") };
                        if (assistant.Thinking.Length > 0)
                        {
                            parts.Add(string.Join("\n", assistant.Thinking.Split('\n').Select(line => "> " + line)));
                        }

                        if (assistant.Text.Length > 0)
                        {
                            parts.Add(assistant.Text);
                        }

                        if (!string.IsNullOrEmpty(assistant.ErrorMessage))
                        {
                            parts.Add("**error:** " + assistant.ErrorMessage);
                        }

                        blocks.Add(string.Join("\n\n", parts));
                        break;
                    }
                case ToolItem tool:
                    {
                        var args = (tool.Args ?? JValue.CreateNull()).ToString(Formatting.Indented).Replace("\r\n", "\n");
                        var parts = new List<string> { $"### Tool `{tool.Name}` — {ToolStatusName(tool.Status)}", Fenced(args) };
                        var output = tool.Result?.Text ?? tool.Partial;
                        if (!string.IsNullOrEmpty(output))
                        {
                            parts.Add(Fenced(output!));
                        }

                        blocks.Add(string.Join("\n\n", parts));
                        break;
                    }
                case NoticeItem notice:
                    blocks.Add($"**{LevelName(notice.Level)}:** {notice.Text}");
                    break;

                case CommandOutputItem output:
                    blocks.Add(Fenced(output.Text));
                    break;
            }
        }

        return string.Join("\n\n", blocks) + "\n";
    }

    /// <summary><c>$0.0215</c> below a dollar, <c>$3.46</c> from a dollar on, judged after rounding to four places.</summary>
    public static string FormatCost(double usd) =>
        "$" + usd.ToString(Math.Round(usd, 4, MidpointRounding.AwayFromZero) < 1 ? "F4" : "F2", Invariant);

    /// <summary><c>850ms</c>, <c>12.3s</c>, <c>2m 5s</c>; the unit is chosen after rounding, so 59 960 ms is <c>1m 0s</c>.</summary>
    public static string FormatDuration(long ms)
    {
        if (ms < 1000)
        {
            return ms.ToString(Invariant) + "ms";
        }

        var tenths = Math.Round(ms / 100d, MidpointRounding.AwayFromZero);
        if (tenths < 600)
        {
            return (tenths / 10).ToString("F1", Invariant) + "s";
        }

        var total = (long)Math.Round(ms / 1000d, MidpointRounding.AwayFromZero);

        return $"{total / 60}m {total % 60}s";
    }

    /// <summary>Compact age of a timestamp: <c>now</c>, <c>5m</c>, <c>3h</c>, <c>2d</c>, <c>1w</c>, <c>4mo</c>, <c>1y</c>.</summary>
    public static string RelativeTime(long epochMs, long nowMs)
    {
        var minutes = (long)Math.Floor((nowMs - epochMs) / 60_000d);
        if (minutes < 1)
        {
            return "now";
        }

        if (minutes < 60)
        {
            return $"{minutes}m";
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return $"{hours}h";
        }

        var days = hours / 24;
        if (days < 7)
        {
            return $"{days}d";
        }

        if (days < 30)
        {
            return $"{days / 7}w";
        }

        if (days < 365)
        {
            return $"{Math.Min(11, days / 30)}mo";
        }

        return $"{days / 365}y";
    }

    /// <summary>Markdown fence longer than any backtick run inside <paramref name="text"/>.</summary>
    public static string FenceFor(string text)
    {
        var longest = 0;
        foreach (Match match in Backticks.Matches(text))
        {
            longest = Math.Max(longest, match.Length);
        }

        return new string('`', Math.Max(3, longest + 1));
    }

    internal static string StatusName(AgentStatus status) => status.ToString().ToLowerInvariant();

    internal static string ToolStatusName(ToolStatus status) => status.ToString().ToLowerInvariant();

    internal static string LevelName(NoticeLevel level) => level.ToString().ToLowerInvariant();

    private static string Fenced(string text)
    {
        var fence = FenceFor(text);

        return $"{fence}\n{text}\n{fence}";
    }

    private static string? Collapse(string? text) => text is null ? null : Whitespace.Replace(text, " ").Trim();

    /// <summary>JavaScript <c>+value.toFixed(digits)</c> rendered back to text: trailing zeros dropped.</summary>
    private static string Trimmed(double value, int digits)
    {
        var fixedText = value.ToString("F" + digits.ToString(Invariant), Invariant);

        return fixedText.IndexOf('.') >= 0 ? fixedText.TrimEnd('0').TrimEnd('.') : fixedText;
    }
}
