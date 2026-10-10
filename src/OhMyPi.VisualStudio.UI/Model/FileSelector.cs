namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A path with the first line of an optional OMP read selector.</summary>
internal readonly struct FileSelector
{
    /// <summary>
    /// Initializes a new instance of the FileSelector struct with the specified file path and optional line number.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    public FileSelector(string path, int? line)
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
