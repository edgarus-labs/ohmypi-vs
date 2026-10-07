namespace Omp.Core.Changes;

public sealed class TrackedChange
{
    /// <summary>Absolute filesystem path.</summary>
    public string Path { get; set; } = "";

    public ChangeStatus Status { get; set; }

    public int Added { get; set; }

    public int Removed { get; set; }
}
