using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

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
