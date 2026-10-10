namespace Omp.Core.Changes;

/// <summary>
/// Represents a tracked modification to a specific path, including its change status and the count of added and removed elements.
/// </summary>
public sealed class TrackedChange
{
    /// <summary>Absolute filesystem path.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public ChangeStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the added.
    /// </summary>
    public int Added { get; set; }

    /// <summary>
    /// Gets or sets the removed.
    /// </summary>
    public int Removed { get; set; }
}
