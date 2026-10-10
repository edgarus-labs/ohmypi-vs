namespace Omp.Core.Session;

/// <summary>Copies of the published view models, so every change publishes a fresh instance.</summary>
internal static class Views
{
    /// <summary>
    /// Creates a shallow copy of the specified session view instance.
    /// </summary>
    /// <param name="v">The v.</param>
    /// <returns>The session view result.</returns>
    public static SessionView Copy(SessionView v) => new SessionView
    {
        Phase = v.Phase,
        SessionId = v.SessionId,
        SessionFile = v.SessionFile,
        SessionName = v.SessionName,
        Model = v.Model,
        ThinkingLevel = v.ThinkingLevel,
        ThinkingSelector = v.ThinkingSelector,
        ThinkingResolved = v.ThinkingResolved,
        AvailableThinkingLevels = v.AvailableThinkingLevels,
        FastModeEnabled = v.FastModeEnabled,
        FastModeActive = v.FastModeActive,
        IsCompacting = v.IsCompacting,
        Queue = v.Queue,
        ContextUsage = v.ContextUsage,
        Todos = v.Todos,
        CostUsd = v.CostUsd,
        LastError = v.LastError,
    };

    /// <summary>
    /// Creates a shallow copy of the specified agent view.
    /// </summary>
    /// <param name="a">The a.</param>
    /// <returns>The agent view result.</returns>
    public static AgentView Copy(AgentView a) => new AgentView
    {
        Id = a.Id,
        ParentId = a.ParentId,
        Name = a.Name,
        Description = a.Description,
        Model = a.Model,
        Status = a.Status,
        Activity = a.Activity,
        ToolCount = a.ToolCount,
        Tokens = a.Tokens,
        CostUsd = a.CostUsd,
        StartedAt = a.StartedAt,
        EndedAt = a.EndedAt,
        SessionFile = a.SessionFile,
        Error = a.Error,
    };

    /// <summary>
    /// Determines whether two AgentView instances are equivalent by comparing all of their properties.
    /// </summary>
    /// <param name="a">The a.</param>
    /// <param name="b">The b.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public static bool Same(AgentView a, AgentView b) =>
        a.Id == b.Id && a.ParentId == b.ParentId && a.Name == b.Name && a.Description == b.Description && a.Model == b.Model &&
        a.Status == b.Status && a.Activity == b.Activity && a.ToolCount == b.ToolCount && a.Tokens == b.Tokens && a.CostUsd == b.CostUsd &&
        a.StartedAt == b.StartedAt && a.EndedAt == b.EndedAt && a.SessionFile == b.SessionFile && a.Error == b.Error;

    /// <summary>
    /// Creates a shallow copy of the specified AssistantItem instance.
    /// </summary>
    /// <param name="i">The i.</param>
    /// <returns>The assistant item result.</returns>
    public static AssistantItem Copy(AssistantItem i) => new AssistantItem
    {
        Id = i.Id,
        Text = i.Text,
        Thinking = i.Thinking,
        Streaming = i.Streaming,
        Model = i.Model,
        StopReason = i.StopReason,
        ErrorMessage = i.ErrorMessage,
        Usage = i.Usage,
    };

    /// <summary>
    /// Creates a shallow copy of the specified tool item.
    /// </summary>
    /// <param name="i">The i.</param>
    /// <returns>The tool item result.</returns>
    public static ToolItem Copy(ToolItem i) => new ToolItem
    {
        Id = i.Id,
        Name = i.Name,
        Args = i.Args,
        Status = i.Status,
        Partial = i.Partial,
        Result = i.Result,
        StartedAt = i.StartedAt,
        EndedAt = i.EndedAt,
    };
}
