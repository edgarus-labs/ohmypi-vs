using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Agents: one row per agent (<c>name  type · status · activity</c>), subagents nested, running first; a row opens
/// its detail. Rows and details are kept by agent id and updated in place, so progress never moves keyboard focus.
/// </summary>
internal sealed class AgentsSection : Section
{
    private readonly IAgentActions _actions;
    private readonly Dictionary<string, Button> _rows = new Dictionary<string, Button>(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentDetail> _details = new Dictionary<string, AgentDetail>(StringComparer.Ordinal);
    private readonly UIElement _empty = EmptyRow("No agents yet.");
    private IReadOnlyList<AgentView> _agents = Array.Empty<AgentView>();

    public AgentsSection(IAgentActions actions) : base("Agents")
    {
        _actions = actions;
    }

    public void Render(IReadOnlyList<AgentView> agents)
    {
        _agents = agents;
        SetTitle(AgentRows.Title(agents));
        foreach (var stale in _details.Keys.Where(id => agents.All(a => a.Id != id)).ToList())
        {
            _details.Remove(stale);
        }

        foreach (var stale in _rows.Keys.Where(id => agents.All(a => a.Id != id)).ToList())
        {
            _rows.Remove(stale);
        }

        var shown = new List<UIElement>();
        foreach (var row in AgentRows.Tree(agents))
        {
            var id = row.Agent.Id;
            if (!_rows.TryGetValue(id, out var button))
            {
                button = new Button { Tag = id }.Styled("Omp.RowButton");
                button.Click += (_, __) => Toggle(id);
                _rows[id] = button;
            }
            UpdateRow(button, row);
            shown.Add(button);
            if (_details.TryGetValue(id, out var detail) && detail.Visibility == Visibility.Visible)
            {
                detail.Update(row.Agent);
                shown.Add(detail);
            }
        }
        if (agents.Count == 0)
        {
            shown.Add(_empty);
        }

        Reconcile(Rows.Children, shown);
    }

    /// <summary>Makes <paramref name="children"/> equal to <paramref name="wanted"/>, leaving elements already in place untouched.</summary>
    private static void Reconcile(UIElementCollection children, IReadOnlyList<UIElement> wanted)
    {
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < children.Count && ReferenceEquals(children[i], wanted[i]))
            {
                continue;
            }

            var at = children.IndexOf(wanted[i]);
            if (at >= 0)
            {
                children.RemoveAt(at);
            }

            children.Insert(i, wanted[i]);
        }
        while (children.Count > wanted.Count)
        {
            children.RemoveAt(children.Count - 1);
        }
    }

    private static void UpdateRow(Button button, AgentRow row)
    {
        var agent = row.Agent;
        var (glyph, brush) = StatusIcon(agent.Status);
        var icon = Ui.Icon(glyph, brush, 11);
        icon.Margin = new Thickness(0, 0, 6, 0);
        var label = Ui.Text(AgentRows.Label(agent), weight: FontWeights.SemiBold, small: true);
        label.Margin = new Thickness(0, 0, 8, 0);
        var description = Ui.Muted(AgentRows.Description(agent));
        var content = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(label, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(label);
        content.Children.Add(description);
        button.Content = content;
        button.Margin = new Thickness(row.Depth * 14, 0, 0, 0);
        button.ToolTip = Format.AgentSummary(agent, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Ui.AutomationName(button, $"{AgentRows.Label(agent)} {AgentRows.Description(agent)}");
    }

    private void Toggle(string id)
    {
        if (_details.TryGetValue(id, out var existing) && existing.Visibility == Visibility.Visible)
        {
            existing.Visibility = Visibility.Collapsed;
            Render(_agents);

            return;
        }
        var agent = _agents.FirstOrDefault(a => a.Id == id);
        if (agent is null)
        {
            return;
        }

        if (existing is null)
        {
            existing = new AgentDetail(agent, _actions);
            _details[id] = existing;
        }
        existing.Visibility = Visibility.Visible;
        Render(_agents);
    }

    internal static (string Glyph, object Brush) StatusIcon(AgentStatus status)
    {
        switch (status)
        {
            case AgentStatus.Running: return (Glyphs.Sync, ThemeKeys.Progress);
            case AgentStatus.Pending: return (Glyphs.Clock, ThemeKeys.Muted);
            case AgentStatus.Completed: return (Glyphs.Check, ThemeKeys.Success);
            case AgentStatus.Failed: return (Glyphs.Error, ThemeKeys.Error);
            default: return (Glyphs.Blocked, ThemeKeys.Muted);
        }
    }

    internal static UIElement EmptyRow(string text)
    {
        var block = Ui.Muted(text);
        block.Margin = new Thickness(22, 2, 0, 2);

        return block;
    }
}
