namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file a tool call names: the path as OMP wrote it, a line to open at, and the old text the tool recorded.</summary>
internal sealed class ToolFile
{
    public ToolFile(string path, int? line, string? oldText)
    {
        Path = path;
        Line = line;
        OldText = oldText;
    }

    public string Path { get; }

    public int? Line { get; }

    public string? OldText { get; }
}
