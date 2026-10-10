using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Omp.Core.Session;

/// <summary>
/// Reads the text OMP's <c>/usage</c> command prints into providers and their rate limits. A provider is an
/// unindented line; a limit is a <c>- name</c> line followed by <c>account: … used (N% left)</c> and optionally
/// <c>resets in …</c>. Entries without a used share (such as saved resets) are left out.
/// </summary>
internal static class UsageReport
{
    private static readonly Regex Ansi = new Regex("\u001b\\[[0-9;]*m", RegexOptions.CultureInvariant);

    private static readonly Regex Used = new Regex(
        @"^\s+(?<account>.+?): [\d.]+(?:%| \S+) used \((?<left>[\d.]+)% left\)(?<inUse>.*in use by this session)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex Resets = new Regex(@"^\s+resets (?<when>.+)$", RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="text"/> is a usage report, even one that lists no providers.</summary>
    public static bool IsReport(string text) => !string.IsNullOrEmpty(text) && text.Contains("Usage (");

    /// <summary>
    /// Parses a raw report string into a collection of provider usage details and their associated usage limits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A collection of iread only list items.</returns>
    public static IReadOnlyList<ProviderUsage> Parse(string text)
    {
        var providers = new List<ProviderUsage>();
        if (!IsReport(text))
        {
            return providers;
        }

        List<UsageLimit>? limits = null;
        string? limitName = null;
        UsageLimit? last = null;
        foreach (var raw in Ansi.Replace(text, "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("Usage (", StringComparison.Ordinal))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]) && !line.StartsWith("- ", StringComparison.Ordinal))
            {
                limits = new List<UsageLimit>();
                providers.Add(new ProviderUsage { Provider = line.Trim(), Limits = limits });
                limitName = null;
                last = null;
                continue;
            }
            if (limits is null)
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                limitName = line.Substring(2).Trim();
                last = null;
                continue;
            }
            var used = Used.Match(line);
            if (used.Success && limitName is not null)
            {
                last = new UsageLimit
                {
                    Name = limitName,
                    Account = used.Groups["account"].Value.Trim(),
                    UsedPercent = Math.Round(100 - double.Parse(used.Groups["left"].Value, CultureInfo.InvariantCulture), 2),
                    InUse = used.Groups["inUse"].Success,
                };
                limits.Add(last);
                continue;
            }
            var resets = Resets.Match(line);
            if (resets.Success && last is not null)
            {
                last.Resets = resets.Groups["when"].Value.Trim();
            }
        }

        return providers;
    }
}
