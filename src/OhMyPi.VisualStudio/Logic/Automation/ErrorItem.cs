namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents a detailed error entry containing severity, diagnostic codes, descriptive messages, and source location information.
/// </summary>
internal sealed class ErrorItem
{
    /// <summary>error, warning or message.</summary>
    public string Severity { get; set; } = "error";

    /// <summary>
    /// Gets or sets the code.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the line.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the column.
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the project.
    /// </summary>
    public string? Project { get; set; }
}
