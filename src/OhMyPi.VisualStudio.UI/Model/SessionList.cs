using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Provides utility methods for filtering and retrieving a limited set of recent sessions.
/// </summary>
internal static class SessionList
{
    /// <summary>
    /// The recent limit.
    /// </summary>
    private const int RecentLimit = 5;

    /// <summary>The newest saved sessions other than <paramref name="currentFile"/> (input newest first) and how many there are.</summary>
    public static RecentSessions Recent(IReadOnlyList<SessionSummary> summaries, string? currentFile)
    {
        var others = summaries.Where(s => !string.Equals(s.Path, currentFile, StringComparison.OrdinalIgnoreCase)).ToList();

        return new RecentSessions(others.Take(RecentLimit).ToList(), others.Count);
    }

    /// <summary>
    /// Filters a collection of session summaries based on a search query and a specified title selector.
    /// </summary>
    /// <param name="summaries">The collection of summaries.</param>
    /// <param name="query">The query.</param>
    /// <param name="title">The title.</param>
    /// <returns>A collection of iread only list items.</returns>
    public static IReadOnlyList<SessionSummary> Filter(IReadOnlyList<SessionSummary> summaries, string query, Func<SessionSummary, string> title)
    {
        var terms = PickerSearch.Terms(query);

        return summaries.Where(s => PickerSearch.Matches(terms, title(s), s.Cwd, s.FirstMessage)).ToList();
    }
}
