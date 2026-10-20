using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Omp.Core.Session;

/// <summary>
/// Names the quota bucket a limit belongs to. OMP's <c>/usage</c> text labels every Antigravity Gemini bucket
/// "Gemini", so the weekly limits of Gemini and of Gemini 4 Argon read alike; <c>omp usage --json</c> carries the
/// bucket in each limit's id (<c>…:gemini-argon-wtu-weekly</c>). Each text limit is paired with the JSON limit of the
/// same provider, name and used share, and takes the bucket's words as part of its name.
/// </summary>
internal static class UsageBuckets
{
    /// <summary>The share the text may differ from the JSON by: the text prints what is left to one decimal.</summary>
    private const double UsedTolerance = 0.06;

    /// <summary>Id words that name the meter's unit or window, not a bucket.</summary>
    private static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wtu", "weekly", "daily", "monthly", "5h", "7d" };

    /// <summary>
    /// Renames the limits in <paramref name="providers"/> whose bucket <paramref name="json"/> (the output of
    /// <c>omp usage --json</c>) names; every other limit, and every limit when the JSON is unusable, keeps its name.
    /// </summary>
    public static void Qualify(IReadOnlyList<ProviderUsage> providers, string json)
    {
        var reported = Read(json);
        if (reported is null)
        {
            return;
        }

        foreach (var provider in providers)
        {
            if (!reported.TryGetValue(Key(provider.Provider), out var candidates))
            {
                continue;
            }

            var taken = new HashSet<ReportedLimit>();
            foreach (var limit in provider.Limits)
            {
                var match = candidates.FirstOrDefault(candidate => !taken.Contains(candidate)
                    && candidate.Name == limit.Name
                    && Math.Abs(candidate.UsedPercent - limit.UsedPercent) <= UsedTolerance);
                if (match is null)
                {
                    continue;
                }

                taken.Add(match);
                limit.Name = match.QualifiedName;
            }
        }
    }

    private static Dictionary<string, List<ReportedLimit>>? Read(string json)
    {
        JObject? root;
        try
        {
            root = JToken.Parse(json) as JObject;
        }
        catch (Exception error) when (error is Newtonsoft.Json.JsonException || error is ArgumentException)
        {
            return null;
        }

        if (root?["reports"] is not JArray reports)
        {
            return null;
        }

        var byProvider = new Dictionary<string, List<ReportedLimit>>();
        foreach (var report in reports.OfType<JObject>())
        {
            var provider = Json.Str(report, "provider");
            if (provider is null || report["limits"] is not JArray limits)
            {
                continue;
            }

            if (!byProvider.TryGetValue(Key(provider), out var list))
            {
                list = new List<ReportedLimit>();
                byProvider[Key(provider)] = list;
            }

            list.AddRange(limits.OfType<JObject>().Select(ReportedLimit.From).OfType<ReportedLimit>());
        }

        return byProvider;
    }

    /// <summary>"Google Antigravity" in the text and "google-antigravity" in the JSON name the same provider.</summary>
    private static string Key(string provider) => new string([.. provider.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private sealed class ReportedLimit
    {
        private ReportedLimit(string name, string qualifiedName, double usedPercent)
        {
            Name = name;
            QualifiedName = qualifiedName;
            UsedPercent = usedPercent;
        }

        /// <summary>The name OMP's text gives the limit.</summary>
        public string Name { get; }

        /// <summary>The name with the bucket's words after the label, or <see cref="Name"/> when the id names none.</summary>
        public string QualifiedName { get; }

        public double UsedPercent { get; }

        public static ReportedLimit? From(JObject limit)
        {
            var label = Json.Str(limit, "label");
            var used = Json.Num(Json.Get(limit, "amount"), "usedFraction");
            if (label is null || used is null)
            {
                return null;
            }

            var tierValue = Json.Str(Json.Get(limit, "scope"), "tier");
            var tier = !string.IsNullOrEmpty(tierValue) && label.IndexOf(tierValue, StringComparison.OrdinalIgnoreCase) < 0 ? $" ({tierValue})" : "";
            var windowLabel = Json.Str(Json.Get(limit, "window"), "label") ?? Json.Str(Json.Get(limit, "scope"), "windowId");
            var suffix = !string.IsNullOrEmpty(windowLabel)
                && !string.Equals(windowLabel, "quota window", StringComparison.OrdinalIgnoreCase)
                && label.IndexOf(windowLabel, StringComparison.OrdinalIgnoreCase) < 0
                ? $" — {windowLabel}"
                : "";
            var bucket = Bucket(Json.Str(limit, "id"), label);

            return new ReportedLimit(
                label + tier + suffix,
                label + (bucket.Length == 0 ? "" : " " + bucket) + tier + suffix,
                used.Value * 100);
        }

        /// <summary>
        /// The words of the last segment of <paramref name="id"/> that follow a word of <paramref name="label"/> and
        /// are not a window or unit: <c>gemini-argon-wtu-weekly</c> under "Gemini" is "Argon". An id that does not start
        /// with a word of the label names no bucket.
        /// </summary>
        private static string Bucket(string? id, string label)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "";
            }

            var words = id!.Substring(id.LastIndexOf(':') + 1).Split(['-'], StringSplitOptions.RemoveEmptyEntries);
            var labelWords = new HashSet<string>(label.Split(' ', '&', '(', ')').Where(word => word.Length > 0), StringComparer.OrdinalIgnoreCase);
            if (words.Length == 0 || !labelWords.Contains(words[0]))
            {
                return "";
            }

            var named = words.Skip(1).Where(word => !Noise.Contains(word) && !labelWords.Contains(word)).ToArray();

            return string.Join(" ", named.Select(Capitalize));
        }

        private static string Capitalize(string word)
        {
            var text = new StringBuilder(word.ToLowerInvariant());
            text[0] = char.ToUpperInvariant(text[0]);

            return text.ToString();
        }
    }
}
