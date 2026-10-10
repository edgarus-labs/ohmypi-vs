namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the metadata and file system location details for a specific project.
/// </summary>
internal sealed class ProjectInfo
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public string? Kind { get; set; }
}
