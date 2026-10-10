namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A run of one line of code: its text, token kind and whether it is a search match.</summary>
internal readonly struct CodeSegment
{
    public CodeSegment(string text, CodeTokenKind kind, bool marked)
    {
        Text = text;
        Kind = kind;
        Marked = marked;
    }

    public string Text { get; }

    public CodeTokenKind Kind { get; }

    public bool Marked { get; }
}
