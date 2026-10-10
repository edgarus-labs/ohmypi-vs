namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the content and positional metadata of a document&apos;s text, including its file path, line range, and origin.
/// </summary>
internal sealed class DocumentText
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
    /// Gets or sets the end line.
    /// </summary>
    public int EndLine { get; set; }

    /// <summary>
    /// Gets or sets the total lines.
    /// </summary>
    public int TotalLines { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>True when the text comes from the editor buffer, false when from disk.</summary>
    public bool FromEditor { get; set; }
}
