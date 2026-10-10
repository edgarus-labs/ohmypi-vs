namespace Omp.Core;

/// <summary>
/// Represents the data structure containing summary information for a session, including its file system path, metadata, and size.
/// </summary>
public sealed class SessionSummary
{
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the first message.
    /// </summary>
    public string? FirstMessage { get; set; }

    /// <summary>
    /// Gets or sets the cwd.
    /// </summary>
    public string? Cwd { get; set; }

    /// <summary>Unix milliseconds.</summary>
    public long Modified { get; set; }

    /// <summary>
    /// Gets or sets the size.
    /// </summary>
    public long Size { get; set; }
}
