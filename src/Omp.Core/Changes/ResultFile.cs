namespace Omp.Core.Changes;

/// <summary>A file named by a tool result's <c>details</c>, with the content it had before the tool ran when reported.</summary>
public sealed class ResultFile
{
    /// <summary>
    /// Initializes a new instance of the ResultFile class with the specified file path and optional previous text content.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="oldText">The old text.</param>
    public ResultFile(string path, string? oldText)
    {
        Path = path;
        OldText = oldText;
    }

    /// <summary>
    /// Gets the path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets or sets the old text.
    /// </summary>
    public string? OldText { get; set; }
}
