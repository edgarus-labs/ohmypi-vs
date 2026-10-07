namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class DebugState
{
    /// <summary>design, run or break.</summary>
    public string Mode { get; set; } = "design";

    public string? Location { get; set; }

    public string? Exception { get; set; }
}
