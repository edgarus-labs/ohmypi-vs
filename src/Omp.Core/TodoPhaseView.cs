using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class TodoPhaseView
{
    public string Name { get; set; } = "";

    public IReadOnlyList<TodoTaskView> Tasks { get; set; } = Array.Empty<TodoTaskView>();
}
