namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file a tool call names: the path as OMP wrote it, a line to open at, and the old text the tool recorded.</summary>
internal sealed class ToolFile
{
    /// <summary>
    /// Initializes a new instance of the ToolFile class with the specified file path, line number, and original text content.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="oldText">The old text.</param>
    public ToolFile(string path, int? line, string? oldText)
    {
        Path = path;
        Line = line;
        OldText = oldText;
    }

    /// <summary>
    /// Gets the path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the line.
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// Gets the old text.
    /// </summary>
    public string? OldText { get; }
}
