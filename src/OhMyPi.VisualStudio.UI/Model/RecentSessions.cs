using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a collection of recently accessed session summaries and the total count of available sessions.
/// </summary>
internal sealed class RecentSessions
{
    /// <summary>
    /// Initializes a new instance of the RecentSessions class with the specified list of session summaries and the total session count.
    /// </summary>
    /// <param name="sessions">The collection of sessions.</param>
    /// <param name="total">The total.</param>
    public RecentSessions(IReadOnlyList<SessionSummary> sessions, int total)
    {
        Sessions = sessions;
        Total = total;
    }

    /// <summary>
    /// Gets the collection of sessions.
    /// </summary>
    public IReadOnlyList<SessionSummary> Sessions { get; }

    /// <summary>Saved sessions other than the current one.</summary>
    public int Total { get; }
}
