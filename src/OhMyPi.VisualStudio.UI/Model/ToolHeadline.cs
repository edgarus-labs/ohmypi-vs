namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Everything a collapsed tool row shows.</summary>
internal sealed class ToolHeadline
{
    public ToolHeadline(string name, string primary, string glyph, string detail, string? error = null, ToolState state = ToolState.Done)
    {
        Name = name;
        Primary = primary;
        Glyph = glyph;
        Detail = detail;
        Error = error;
        State = state;
    }

    /// <summary>Display name (<c>server › tool</c> for MCP tools).</summary>
    public string Name { get; }

    /// <summary>Primary argument on one line (path, quoted pattern, command, …).</summary>
    public string Primary { get; }

    /// <summary><c>…</c> running or started in the background, <c>✓</c> done, <c>✗</c> failed.</summary>
    public string Glyph { get; }

    public ToolState State { get; }

    /// <summary>Hits / exit code / written lines and duration, joined by <c> · </c>.</summary>
    public string Detail { get; }

    /// <summary>First non-empty line of an error result.</summary>
    public string? Error { get; }
}
