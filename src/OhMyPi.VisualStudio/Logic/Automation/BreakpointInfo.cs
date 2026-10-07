namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class BreakpointInfo
{
    public string Path { get; set; } = "";

    public int Line { get; set; }

    public string? Condition { get; set; }

    public bool Enabled { get; set; }
}
