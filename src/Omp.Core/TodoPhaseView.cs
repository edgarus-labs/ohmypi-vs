using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents a read-only view of a todo phase, including its name and the associated collection of tasks.
/// </summary>
public sealed class TodoPhaseView
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of tasks.
    /// </summary>
    public IReadOnlyList<TodoTaskView> Tasks { get; set; } = Array.Empty<TodoTaskView>();
}
