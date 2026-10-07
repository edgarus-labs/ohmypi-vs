using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>The "agent is working" row kept at the end of the transcript while a prompt is in flight.</summary>
    internal sealed class ActivityItem : TranscriptItem
    {
        public const string RowId = "omp-activity";

        public ActivityItem(string label, long startedAt)
        {
            Id = RowId;
            Label = label;
            StartedAt = startedAt;
        }

        public string Label { get; }

        /// <summary>Unix milliseconds when the activity began.</summary>
        public long StartedAt { get; }
    }

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
}
