using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation;

internal sealed class BuildResult
{
    public bool Succeeded { get; set; }

    public int ProjectsSucceeded { get; set; }

    public int ProjectsFailed { get; set; }

    public int ProjectsSkipped { get; set; }

    public List<ErrorItem> Errors { get; } = new List<ErrorItem>();
}
