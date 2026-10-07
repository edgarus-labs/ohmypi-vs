namespace Omp.Core;

/// <summary>
/// Transcript entry. The service publishes a fresh instance for every change; an item's
/// <see cref="Id"/> is stable so views replace the previous instance with the same id.
/// </summary>
public abstract class TranscriptItem
{
    public string Id { get; set; } = "";
}
