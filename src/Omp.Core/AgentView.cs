namespace Omp.Core;

/// <summary>
/// Represents a read-only view of an agent&apos;s state, including its configuration, execution metrics, and operational status.
/// </summary>
public sealed class AgentView
{
    /// <summary>"main" for the session's own agent; otherwise the OMP subagent id.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the parent id.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>Agent type/name (task, scout, reviewer, ...).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public AgentStatus Status { get; set; }

    /// <summary>Running tool or latest progress line.</summary>
    public string? Activity { get; set; }

    /// <summary>
    /// Gets or sets the tool count.
    /// </summary>
    public int? ToolCount { get; set; }

    /// <summary>
    /// Gets or sets the tokens.
    /// </summary>
    public long? Tokens { get; set; }

    /// <summary>
    /// Gets or sets the cost usd.
    /// </summary>
    public double? CostUsd { get; set; }

    /// <summary>
    /// Gets or sets the started at.
    /// </summary>
    public long? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the ended at.
    /// </summary>
    public long? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the session file.
    /// </summary>
    public string? SessionFile { get; set; }

    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string? Error { get; set; }
}
