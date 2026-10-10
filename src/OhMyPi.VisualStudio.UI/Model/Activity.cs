using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>The "agent is working" row kept at the end of the transcript while a prompt is in flight.</summary>
internal sealed class ActivityItem : TranscriptItem
{
    /// <summary>
    /// The row id.
    /// </summary>
    public const string RowId = "omp-activity";

    /// <summary>
    /// Initializes a new instance of the ActivityItem class with the specified label and start timestamp.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="startedAt">The started at.</param>
    public ActivityItem(string label, long startedAt)
    {
        Id = RowId;
        Label = label;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label { get; }

    /// <summary>Unix milliseconds when the activity began.</summary>
    public long StartedAt { get; }
}
