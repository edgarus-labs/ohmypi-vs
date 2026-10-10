namespace Omp.Core;

/// <summary>
/// Represents the configuration options for starting a session, specifying whether to initiate a new session or resume from a designated session file.
/// </summary>
public sealed class StartOptions
{
    /// <summary>
    /// Gets or sets the resume session file.
    /// </summary>
    public string? ResumeSessionFile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether new session.
    /// </summary>
    public bool NewSession { get; set; }
}
