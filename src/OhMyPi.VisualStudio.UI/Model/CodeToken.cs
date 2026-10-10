namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A run of <see cref="Length"/> characters of code starting at <see cref="Start"/>, all of one <see cref="Kind"/>.</summary>
internal readonly struct CodeToken
{
    /// <summary>
    /// Initializes a new instance of the CodeToken struct with the specified start position, length, and token kind.
    /// </summary>
    /// <param name="start">The start.</param>
    /// <param name="length">The length.</param>
    /// <param name="kind">The kind.</param>
    public CodeToken(int start, int length, CodeTokenKind kind)
    {
        Start = start;
        Length = length;
        Kind = kind;
    }

    /// <summary>
    /// Gets the start.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the length.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public CodeTokenKind Kind { get; }
}
