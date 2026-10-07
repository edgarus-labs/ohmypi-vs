using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

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
