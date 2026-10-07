namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>A replacement of a 0-based text span of a line-based buffer.</summary>
internal sealed class LineEdit
{
    public LineEdit(int startLine, int startIndex, int endLine, int endIndex, string text)
    {
        StartLine = startLine;
        StartIndex = startIndex;
        EndLine = endLine;
        EndIndex = endIndex;
        Text = text;
    }

    public int StartLine { get; }

    public int StartIndex { get; }

    public int EndLine { get; }

    public int EndIndex { get; }

    public string Text { get; }
}
