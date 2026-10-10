namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the metadata and state information for a document, including its file path and current modification status.
/// </summary>
internal sealed class DocumentInfo
{
    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether is dirty.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is read only.
    /// </summary>
    public bool IsReadOnly { get; set; }
}
