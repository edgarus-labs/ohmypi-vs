namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>A replacement of a 0-based text span of a line-based buffer.</summary>
internal sealed class LineEdit
{
    /// <summary>
    /// Initializes a new instance of the LineEdit class with the specified line and index boundaries and the associated text content.
    /// </summary>
    /// <param name="startLine">The start line.</param>
    /// <param name="startIndex">The start index.</param>
    /// <param name="endLine">The end line.</param>
    /// <param name="endIndex">The end index.</param>
    /// <param name="text">The text.</param>
    public LineEdit(int startLine, int startIndex, int endLine, int endIndex, string text)
    {
        StartLine = startLine;
        StartIndex = startIndex;
        EndLine = endLine;
        EndIndex = endIndex;
        Text = text;
    }

    /// <summary>
    /// Gets the start line.
    /// </summary>
    public int StartLine { get; }

    /// <summary>
    /// Gets the start index.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the end line.
    /// </summary>
    public int EndLine { get; }

    /// <summary>
    /// Gets the end index.
    /// </summary>
    public int EndIndex { get; }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }
}
