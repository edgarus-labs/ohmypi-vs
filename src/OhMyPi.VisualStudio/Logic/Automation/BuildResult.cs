using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the outcome of a build process, including success status, project counts, and a collection of encountered errors.
/// </summary>
internal sealed class BuildResult
{
    /// <summary>
    /// Gets or sets a value indicating whether succeeded.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Gets or sets the projects succeeded.
    /// </summary>
    public int ProjectsSucceeded { get; set; }

    /// <summary>
    /// Gets or sets the projects failed.
    /// </summary>
    public int ProjectsFailed { get; set; }

    /// <summary>
    /// Gets or sets the projects skipped.
    /// </summary>
    public int ProjectsSkipped { get; set; }

    /// <summary>
    /// Gets the collection of errors.
    /// </summary>
    public List<ErrorItem> Errors { get; } = new List<ErrorItem>();
}
