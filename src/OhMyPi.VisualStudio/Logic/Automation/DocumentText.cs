namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class DocumentText
{
    public string Path { get; set; } = "";

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    public int TotalLines { get; set; }

    public string Text { get; set; } = "";

    /// <summary>True when the text comes from the editor buffer, false when from disk.</summary>
    public bool FromEditor { get; set; }
}
