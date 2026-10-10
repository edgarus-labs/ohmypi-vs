using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents the configuration and metadata of a development solution, including its active build settings, startup project, and associated project information.
/// </summary>
internal sealed class SolutionInfo
{
    /// <summary>Null when no solution is open.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the active configuration.
    /// </summary>
    public string? ActiveConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the active platform.
    /// </summary>
    public string? ActivePlatform { get; set; }

    /// <summary>
    /// Gets or sets the startup project.
    /// </summary>
    public string? StartupProject { get; set; }

    /// <summary>
    /// Gets or sets the active document.
    /// </summary>
    public string? ActiveDocument { get; set; }

    /// <summary>
    /// Gets the collection of projects.
    /// </summary>
    public List<ProjectInfo> Projects { get; } = new List<ProjectInfo>();
}
