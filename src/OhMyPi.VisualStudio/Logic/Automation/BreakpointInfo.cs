namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the configuration details of a debug breakpoint, including its source file path, line number, optional condition, and enabled state.
/// </summary>
internal sealed class BreakpointInfo
{
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets the line.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the condition.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
