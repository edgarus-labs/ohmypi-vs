namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class SelectionInfo
{
    public string Path { get; set; } = "";

    public int StartLine { get; set; }

    public int StartColumn { get; set; }

    public int EndLine { get; set; }

    public int EndColumn { get; set; }

    public string Text { get; set; } = "";
}
