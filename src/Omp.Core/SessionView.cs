using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>Immutable snapshot published by the service; consumers must not mutate it.</summary>
public sealed class SessionView
{
    public SessionPhase Phase { get; set; }

    public string? SessionId { get; set; }

    public string? SessionFile { get; set; }

    public string? SessionName { get; set; }

    public ModelView? Model { get; set; }

    /// <summary>Effective level OMP uses; with auto selected, the provisional or resolved effort.</summary>
    public string? ThinkingLevel { get; set; }

    /// <summary>The user's selector (OMP "configured"): auto or an explicit level.</summary>
    public string? ThinkingSelector { get; set; }

    /// <summary>The effort auto resolved to for the current turn.</summary>
    public string? ThinkingResolved { get; set; }

    /// <summary>off, auto for reasoning models, then the model's efforts.</summary>
    public IReadOnlyList<string> AvailableThinkingLevels { get; set; } = Array.Empty<string>();

    public bool? FastModeEnabled { get; set; }

    public bool? FastModeActive { get; set; }

    public bool IsCompacting { get; set; }

    public QueueView Queue { get; set; } = new QueueView();

    public ContextUsageView? ContextUsage { get; set; }

    public IReadOnlyList<TodoPhaseView> Todos { get; set; } = Array.Empty<TodoPhaseView>();

    public double? CostUsd { get; set; }

    public string? LastError { get; set; }
}
