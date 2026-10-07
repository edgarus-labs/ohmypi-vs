using System;
using System.Collections.Generic;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>Splits search text into words that must all match.</summary>
    internal static class PickerSearch
    {
        internal static string[] Terms(string query) => query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        internal static bool Matches(string[] terms, params string?[] fields) =>
            terms.All(term => fields.Any(field => field != null && field.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0));
    }

    internal sealed class RecentSessions
    {
        public RecentSessions(IReadOnlyList<SessionSummary> sessions, int total)
        {
            Sessions = sessions;
            Total = total;
        }

        public IReadOnlyList<SessionSummary> Sessions { get; }
        /// <summary>Saved sessions other than the current one.</summary>
        public int Total { get; }
    }

    internal static class SessionList
    {
        private const int RecentLimit = 5;

        /// <summary>The newest saved sessions other than <paramref name="currentFile"/> (input newest first) and how many there are.</summary>
        public static RecentSessions Recent(IReadOnlyList<SessionSummary> summaries, string? currentFile)
        {
            var others = summaries.Where(s => !string.Equals(s.Path, currentFile, StringComparison.OrdinalIgnoreCase)).ToList();
            return new RecentSessions(others.Take(RecentLimit).ToList(), others.Count);
        }

        public static IReadOnlyList<SessionSummary> Filter(IReadOnlyList<SessionSummary> summaries, string query, Func<SessionSummary, string> title)
        {
            var terms = PickerSearch.Terms(query);
            return summaries.Where(s => PickerSearch.Matches(terms, title(s), s.Cwd, s.FirstMessage)).ToList();
        }
    }
}
