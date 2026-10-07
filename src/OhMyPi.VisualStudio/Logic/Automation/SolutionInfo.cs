using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class SolutionInfo
{
    /// <summary>Null when no solution is open.</summary>
    public string? Path { get; set; }

    public string? ActiveConfiguration { get; set; }

    public string? ActivePlatform { get; set; }

    public string? StartupProject { get; set; }

    public string? ActiveDocument { get; set; }

    public List<ProjectInfo> Projects { get; } = new List<ProjectInfo>();
}
