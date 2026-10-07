using System;
using System.Collections.Generic;
using System.Linq;
using Omp.Core;
using Omp.Core.Changes;

namespace OhMyPi.VisualStudio.UI.Model
{
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

    /// <summary>Agents section rows: labels, compact descriptions and the agent tree with subagents nested under their parents.</summary>
    internal static class AgentRows
    {
        private const int DescriptionMax = 48;

        public static string StatusText(AgentStatus status) => status.ToString().ToLowerInvariant();

        /// <summary>Subagents by their OMP name (the last segment of a nested <c>Parent.Child</c> id); <c>main</c> unchanged.</summary>
        public static string Label(AgentView agent)
        {
            if (agent.Id == "main") return agent.Name;
            var label = agent.Id.Substring(agent.Id.LastIndexOf('.') + 1);
            return label.Length > 0 ? label : agent.Name;
        }

        /// <summary>Compact row description: <c>type · status · first line of activity</c>, truncated.</summary>
        public static string Description(AgentView agent)
        {
            var activity = agent.Activity?.Split('\n')[0].Trim();
            var parts = new List<string> { StatusText(agent.Status) };
            if (Label(agent) != agent.Name) parts.Insert(0, agent.Name);
            if (!string.IsNullOrEmpty(activity)) parts.Add(activity!);
            var text = string.Join(" · ", parts);
            return text.Length > DescriptionMax ? text.Substring(0, DescriptionMax - 1) + "…" : text;
        }

        private static int Order(AgentStatus status) => status == AgentStatus.Running ? 0 : status == AgentStatus.Pending ? 1 : 2;

        /// <summary>Running agents first, then pending; stable otherwise.</summary>
        public static IReadOnlyList<AgentView> Sort(IReadOnlyList<AgentView> agents) =>
            agents.Select((agent, index) => (agent, index)).OrderBy(p => Order(p.agent.Status)).ThenBy(p => p.index).Select(p => p.agent).ToList();

        /// <summary><c>Agents 3 · 2 running</c>.</summary>
        public static string Title(IReadOnlyList<AgentView> agents)
        {
            if (agents.Count == 0) return "Agents";
            var running = agents.Count(agent => agent.Status == AgentStatus.Running);
            return running > 0 ? $"Agents {agents.Count} · {running} running" : $"Agents {agents.Count}";
        }

        /// <summary>Depth-first rows: subagents nested under their declared parent, else under main; running first among siblings.</summary>
        public static IReadOnlyList<AgentRow> Tree(IReadOnlyList<AgentView> agents)
        {
            var ids = new HashSet<string>(agents.Select(agent => agent.Id), StringComparer.Ordinal);
            string? ParentOf(AgentView agent)
            {
                if (agent.Id == "main") return null;
                if (!string.IsNullOrEmpty(agent.ParentId) && agent.ParentId != agent.Id && ids.Contains(agent.ParentId!)) return agent.ParentId;
                return ids.Contains("main") ? "main" : null;
            }

            var rows = new List<AgentRow>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Visit(string? parent, int depth)
            {
                foreach (var child in Sort(agents.Where(agent => ParentOf(agent) == parent).ToList()))
                {
                    if (!visited.Add(child.Id)) continue;
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

    /// <summary>Changes section rows: title, status letter, file name and folder detail.</summary>
    internal static class ChangeRows
    {
        public static string Summary(int count) => count == 0 ? "" : $"{count} {(count == 1 ? "file" : "files")}";

        public static string Title(int count) => count == 0 ? "Changes" : $"Changes · {Summary(count)}";

        public static string StatusLetter(ChangeStatus status) =>
            status == ChangeStatus.Added ? "A" : status == ChangeStatus.Deleted ? "D" : "M";

        public static string StatusText(ChangeStatus status) =>
            status == ChangeStatus.Added ? "added" : status == ChangeStatus.Deleted ? "deleted" : "modified";

        public static string FileName(TrackedChange change) => System.IO.Path.GetFileName(change.Path);

        /// <summary><c>src\auth · +3 −1</c>: the folder relative to <paramref name="cwd"/> when inside it, and the line counts.</summary>
        public static string Detail(TrackedChange change, string? cwd)
        {
            var dir = System.IO.Path.GetDirectoryName(change.Path) ?? "";
            var shown = cwd == null ? dir : RelativeDir(dir, cwd);
            var parts = new List<string>();
            if (shown.Length > 0 && shown != ".") parts.Add(shown);
            if (change.Status != ChangeStatus.Deleted) parts.Add($"+{change.Added} −{change.Removed}");
            return string.Join(" · ", parts);
        }

        private static string RelativeDir(string dir, string cwd)
        {
            var root = cwd.TrimEnd('\\', '/');
            if (string.Equals(dir.TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase)) return "";
            return dir.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || dir.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
                ? dir.Substring(root.Length + 1)
                : dir;
        }
    }
}
