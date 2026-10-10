namespace Omp.Core;

/// <summary>
/// Represents a base request for an interaction, encapsulating a unique identifier and an optional timeout duration.
/// </summary>
public abstract class InteractionRequest
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the timeout ms.
    /// </summary>
    public int? TimeoutMs { get; set; }
}
