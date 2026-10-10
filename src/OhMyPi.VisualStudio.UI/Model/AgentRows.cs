using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Agents section rows: labels, compact descriptions and the agent tree with subagents nested under their parents.</summary>
internal static class AgentRows
{
    /// <summary>
    /// The description max.
    /// </summary>
    private const int DescriptionMax = 48;

    /// <summary>
    /// Converts the specified agent status to its lowercase string representation.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The string result.</returns>
    public static string StatusText(AgentStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>Subagents by their OMP name (the last segment of a nested <c>Parent.Child</c> id); <c>main</c> unchanged.</summary>
    public static string Label(AgentView agent)
    {
        if (agent.Id == "main")
        {
            return agent.Name;
        }

        var label = agent.Id.Substring(agent.Id.LastIndexOf('.') + 1);

        return label.Length > 0 ? label : agent.Name;
    }

    /// <summary>Compact row description: <c>type · status · first line of activity</c>, truncated.</summary>
    public static string Description(AgentView agent)
    {
        var activity = agent.Activity?.Split('\n')[0].Trim();
        var parts = new List<string> { StatusText(agent.Status) };
        if (Label(agent) != agent.Name)
        {
            parts.Insert(0, agent.Name);
        }

        if (!string.IsNullOrEmpty(activity))
        {
            parts.Add(activity!);
        }

        var text = string.Join(" · ", parts);

        return text.Length > DescriptionMax ? text.Substring(0, DescriptionMax - 1) + "…" : text;
    }

    /// <summary>
    /// Determines the sorting priority of an agent status, assigning the lowest value to running agents and the highest value to inactive or stopped agents.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The int result.</returns>
    private static int Order(AgentStatus status) => status == AgentStatus.Running ? 0 : status == AgentStatus.Pending ? 1 : 2;

    /// <summary>Running agents first, then pending; stable otherwise.</summary>
    public static IReadOnlyList<AgentView> Sort(IReadOnlyList<AgentView> agents) =>
        agents.Select((agent, index) => (agent, index)).OrderBy(p => Order(p.agent.Status)).ThenBy(p => p.index).Select(p => p.agent).ToList();

    /// <summary><c>Agents 3 · 2 running</c>.</summary>
    public static string Title(IReadOnlyList<AgentView> agents)
    {
        if (agents.Count == 0)
        {
            return "Agents";
        }

        var running = agents.Count(agent => agent.Status == AgentStatus.Running);

        return running > 0 ? $"Agents {agents.Count} · {running} running" : $"Agents {agents.Count}";
    }

    /// <summary>Depth-first rows: subagents nested under their declared parent, else under main; running first among siblings.</summary>
    public static IReadOnlyList<AgentRow> Tree(IReadOnlyList<AgentView> agents)
    {
        var ids = new HashSet<string>(agents.Select(agent => agent.Id), StringComparer.Ordinal);
        string? ParentOf(AgentView agent)
        {
            if (agent.Id == "main")
            {
                return null;
            }

            if (!string.IsNullOrEmpty(agent.ParentId) && agent.ParentId != agent.Id && ids.Contains(agent.ParentId!))
            {
                return agent.ParentId;
            }

            return ids.Contains("main") ? "main" : null;
        }

        var rows = new List<AgentRow>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string? parent, int depth)
        {
            foreach (var child in Sort(agents.Where(agent => ParentOf(agent) == parent).ToList()))
            {
                if (!visited.Add(child.Id))
                {
                    continue;
                }

                rows.Add(new AgentRow(child, depth));
                Visit(child.Id, depth + 1);
            }
        }

        Visit(null, 0);
        foreach (var orphan in agents.Where(agent => !visited.Contains(agent.Id)).ToList())
        {
            rows.Add(new AgentRow(orphan, 0));
            visited.Add(orphan.Id);
        }

        return rows;
    }
}
