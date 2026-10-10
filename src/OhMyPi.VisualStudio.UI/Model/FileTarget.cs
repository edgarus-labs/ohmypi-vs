namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A file a chat message points at, with an optional 1-based line.</summary>
internal sealed class FileTarget
{
    /// <summary>
    /// Initializes a new instance of the FileTarget class with the specified file path and optional line number.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    public FileTarget(string path, int? line)
    {
        Path = path;
        Line = line;
    }

    /// <summary>
    /// Gets the path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the line.
    /// </summary>
    public int? Line { get; }
}
