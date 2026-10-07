using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class AgentRow
{
    public AgentRow(AgentView agent, int depth)
    {
        Agent = agent;
        Depth = depth;
    }

    public AgentView Agent { get; }

    public int Depth { get; }
}
