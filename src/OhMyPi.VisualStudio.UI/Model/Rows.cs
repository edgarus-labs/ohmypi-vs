using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a data row containing an agent view and its associated depth level.
/// </summary>
internal sealed class AgentRow
{
    /// <summary>
    /// Initializes a new instance of the AgentRow class with the specified agent view and hierarchy depth.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="depth">The depth.</param>
    public AgentRow(AgentView agent, int depth)
    {
        Agent = agent;
        Depth = depth;
    }

    /// <summary>
    /// Gets the agent.
    /// </summary>
    public AgentView Agent { get; }

    /// <summary>
    /// Gets the depth.
    /// </summary>
    public int Depth { get; }
}
