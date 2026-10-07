namespace Omp.Core;

public sealed class TodoTaskView
{
    public string Content { get; set; } = "";

    /// <summary>pending | in_progress | completed | abandoned | blocked | other OMP value.</summary>
    public string Status { get; set; } = "pending";
}
