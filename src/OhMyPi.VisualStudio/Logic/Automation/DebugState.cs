namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the current debugging state, including the operational mode, source location, and any associated exception details.
/// </summary>
internal sealed class DebugState
{
    /// <summary>design, run or break.</summary>
    public string Mode { get; set; } = "design";

    /// <summary>
    /// Gets or sets the location.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the exception.
    /// </summary>
    public string? Exception { get; set; }
}
