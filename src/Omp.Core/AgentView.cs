namespace Omp.Core;

public sealed class AgentView
{
    /// <summary>"main" for the session's own agent; otherwise the OMP subagent id.</summary>
    public string Id { get; set; } = "";

    public string? ParentId { get; set; }

    /// <summary>Agent type/name (task, scout, reviewer, ...).</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public string? Model { get; set; }

    public AgentStatus Status { get; set; }

    /// <summary>Running tool or latest progress line.</summary>
    public string? Activity { get; set; }

    public int? ToolCount { get; set; }

    public long? Tokens { get; set; }

    public double? CostUsd { get; set; }

    public long? StartedAt { get; set; }

    public long? EndedAt { get; set; }

    public string? SessionFile { get; set; }

    public string? Error { get; set; }
}
