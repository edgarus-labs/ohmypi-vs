using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

internal static class ActivityText
{
    /// <summary>What the agent is doing for the activity row, or null when nothing is in flight.</summary>
    public static string? Label(SessionView session)
    {
        switch (session.Phase)
        {
            case SessionPhase.Submitting: return "Sending";
            case SessionPhase.Running: return session.IsCompacting ? "Compacting" : "Working";
            case SessionPhase.Yielded: return "Finishing";
            case SessionPhase.Aborting: return "Stopping";
            default: return null;
        }
    }
}
