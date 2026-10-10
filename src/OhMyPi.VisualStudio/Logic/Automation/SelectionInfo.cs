namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the location and content of a selected text range within a specific file path.
/// </summary>
internal sealed class SelectionInfo
{
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets the start line.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the start column.
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// Gets or sets the end line.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the end column.
    /// </summary>
    public int EndColumn { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";
}
