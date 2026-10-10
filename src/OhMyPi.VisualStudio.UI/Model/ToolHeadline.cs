namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Everything a collapsed tool row shows.</summary>
internal sealed class ToolHeadline
{
    /// <summary>
    /// Initializes a new instance of the ToolHeadline class with the specified name, primary text, glyph, detail, error message, and state.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="primary">The primary.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="error">The error.</param>
    /// <param name="state">The state.</param>
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

    /// <summary>
    /// Gets the state.
    /// </summary>
    public ToolState State { get; }

    /// <summary>Hits / exit code / written lines and duration, joined by <c> · </c>.</summary>
    public string Detail { get; }

    /// <summary>First non-empty line of an error result.</summary>
    public string? Error { get; }
}
