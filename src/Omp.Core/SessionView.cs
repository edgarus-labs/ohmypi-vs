using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>Immutable snapshot published by the service; consumers must not mutate it.</summary>
public sealed class SessionView
{
    /// <summary>
    /// Gets or sets the phase.
    /// </summary>
    public SessionPhase Phase { get; set; }

    /// <summary>
    /// Gets or sets the session id.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the session file.
    /// </summary>
    public string? SessionFile { get; set; }

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    public string? SessionName { get; set; }

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    public ModelView? Model { get; set; }

    /// <summary>Effective level OMP uses; with auto selected, the provisional or resolved effort.</summary>
    public string? ThinkingLevel { get; set; }

    /// <summary>The user's selector (OMP "configured"): auto or an explicit level.</summary>
    public string? ThinkingSelector { get; set; }

    /// <summary>The effort auto resolved to for the current turn.</summary>
    public string? ThinkingResolved { get; set; }

    /// <summary>off, auto for reasoning models, then the model's efforts.</summary>
    public IReadOnlyList<string> AvailableThinkingLevels { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the fast mode enabled.
    /// </summary>
    public bool? FastModeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the fast mode active.
    /// </summary>
    public bool? FastModeActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is compacting.
    /// </summary>
    public bool IsCompacting { get; set; }

    /// <summary>
    /// Gets or sets the queue.
    /// </summary>
    public QueueView Queue { get; set; } = new QueueView();

    /// <summary>
    /// Gets or sets the context usage.
    /// </summary>
    public ContextUsageView? ContextUsage { get; set; }

    /// <summary>
    /// Gets or sets the collection of todos.
    /// </summary>
    public IReadOnlyList<TodoPhaseView> Todos { get; set; } = Array.Empty<TodoPhaseView>();

    /// <summary>
    /// Gets or sets the cost usd.
    /// </summary>
    public double? CostUsd { get; set; }

    /// <summary>
    /// Gets or sets the last error.
    /// </summary>
    public string? LastError { get; set; }
}
