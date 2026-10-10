namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A run of one line of code: its text, token kind and whether it is a search match.</summary>
internal readonly struct CodeSegment
{
    /// <summary>
    /// Initializes a new instance of the CodeSegment struct with the specified text, token kind, and marking status.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="marked">The marked.</param>
    public CodeSegment(string text, CodeTokenKind kind, bool marked)
    {
        Text = text;
        Kind = kind;
        Marked = marked;
    }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public CodeTokenKind Kind { get; }

    /// <summary>
    /// Gets a value indicating whether marked.
    /// </summary>
    public bool Marked { get; }
}
