namespace Omp.Core;

/// <summary>
/// Represents a read-only view of a todo task, containing its content and current status.
/// </summary>
public sealed class TodoTaskView
{
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>pending | in_progress | completed | abandoned | blocked | other OMP value.</summary>
    public string Status { get; set; } = "pending";
}
