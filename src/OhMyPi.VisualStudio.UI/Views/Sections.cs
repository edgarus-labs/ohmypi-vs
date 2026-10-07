using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Omp.Core;
using Omp.Core.Changes;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>Collapsible section with a title row (collapsed by default) and a height-capped body.</summary>
    internal abstract class Section : StackPanel
    {
        private readonly ToggleButton _header;
        private readonly TextBlock _chevron;
        private readonly TextBlock _title;

        protected Section(string title)
        {
            _chevron = Ui.Icon(Glyphs.ChevronRight, ThemeKeys.Muted, 9);
            _chevron.Margin = new Thickness(0, 0, 6, 0);
            _title = Ui.Text(title, weight: FontWeights.SemiBold, small: true);
            _header = new ToggleButton { Content = Ui.Row(_chevron, _title) }.Styled("Omp.ExpanderHeader");
            _header.Margin = new Thickness(4, 0, 4, 0);
            Rows = new StackPanel();
            Body = new ScrollViewer
            {
                Content = Rows,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(4, 0, 4, 4),
            };
            _header.Checked += (_, __) => Apply(true);
            _header.Unchecked += (_, __) => Apply(false);
            Children.Add(_header);
            Children.Add(Body);
        }

        protected StackPanel Rows { get; }

        protected ScrollViewer Body { get; }

        public void Expand()
        {
            _header.IsChecked = true;
            _header.BringIntoView();
            _header.Focus();
        }

        protected void SetTitle(string title)
        {
            _title.Text = title;
            Ui.AutomationName(_header, title);
        }

        private void Apply(bool open)
        {
            _chevron.Text = open ? Glyphs.ChevronDown : Glyphs.ChevronRight;
            Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>What the Agents section asks of the chat control.</summary>
    internal interface IAgentActions
    {
        RenderContext RenderContext { get; }
        Task<IReadOnlyList<TranscriptItem>> TranscriptAsync(string agentId);
        Task SteerAsync(string agentId, string message);
        Task<bool> CancelAsync(string agentId);
        /// <summary>Runs a user action, reporting a failure as an error notice.</summary>
        Task RunAsync(string action, Func<Task> work);
        void Notice(NoticeLevel level, string text);
    }

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
            foreach (var stale in _details.Keys.Where(id => agents.All(a => a.Id != id)).ToList()) _details.Remove(stale);
            foreach (var stale in _rows.Keys.Where(id => agents.All(a => a.Id != id)).ToList()) _rows.Remove(stale);
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
            if (agents.Count == 0) shown.Add(_empty);
            Reconcile(Rows.Children, shown);
        }

        /// <summary>Makes <paramref name="children"/> equal to <paramref name="wanted"/>, leaving elements already in place untouched.</summary>
        private static void Reconcile(UIElementCollection children, IReadOnlyList<UIElement> wanted)
        {
            for (var i = 0; i < wanted.Count; i++)
            {
                if (i < children.Count && ReferenceEquals(children[i], wanted[i])) continue;
                var at = children.IndexOf(wanted[i]);
                if (at >= 0) children.RemoveAt(at);
                children.Insert(i, wanted[i]);
            }
            while (children.Count > wanted.Count) children.RemoveAt(children.Count - 1);
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
            if (agent == null) return;
            if (existing == null)
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

    /// <summary>Agent detail: status/model/activity/tools/tokens/cost, its transcript, and Steer / Cancel.</summary>
    internal sealed class AgentDetail : Border
    {
        private readonly IAgentActions _actions;
        private readonly TextBlock _summary;
        private readonly Button _cancel;
        private readonly StackPanel _steerBox;
        private readonly TextBox _steerInput;
        private readonly StackPanel _confirm;
        private readonly ContentControl _transcript = new ContentControl { Focusable = false };
        private AgentView _agent;

        public AgentDetail(AgentView agent, IAgentActions actions)
        {
            _agent = agent;
            _actions = actions;
            Margin = new Thickness(22, 2, 4, 6);
            Padding = new Thickness(10, 6, 10, 8);
            this.Styled("Omp.Card");
            _summary = Ui.Text("", ThemeKeys.Muted, wrap: true, small: true);

            var transcriptButton = Ui.Button("Transcript", () => _ = LoadTranscriptAsync());
            var steerButton = Ui.Button("Steer", ShowSteer);
            _cancel = Ui.Button("Cancel", ShowConfirm);
            var actionsRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var button in new[] { transcriptButton, steerButton, _cancel })
            {
                button.Margin = new Thickness(0, 0, 6, 0);
                actionsRow.Children.Add(button);
            }

            _steerInput = new TextBox { AcceptsReturn = false, MinWidth = 160 }.Styled("Omp.TextBox");
            Ui.AutomationName(_steerInput, "Message for the agent");
            _steerInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    _ = SteerAsync();
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    _steerBox!.Visibility = Visibility.Collapsed;
                }
            };
            var send = Ui.Button("Send", () => _ = SteerAsync(), "Omp.PrimaryButton");
            send.Margin = new Thickness(6, 0, 0, 0);
            var steerRow = new DockPanel();
            DockPanel.SetDock(send, Dock.Right);
            steerRow.Children.Add(send);
            steerRow.Children.Add(_steerInput);
            _steerBox = Ui.Column(2, Ui.Muted("Message for the running agent"), steerRow);
            _steerBox.Margin = new Thickness(0, 6, 0, 0);
            _steerBox.Visibility = Visibility.Collapsed;

            var confirmText = Ui.Text("", wrap: true, small: true);
            var yes = Ui.Button("Cancel Agent", () => _ = CancelAsync(), "Omp.PrimaryButton");
            var no = Ui.Button("Keep Running", () => _confirm!.Visibility = Visibility.Collapsed);
            yes.Margin = new Thickness(0, 0, 6, 0);
            _confirm = Ui.Column(4, confirmText, Ui.Row(yes, no));
            _confirm.Margin = new Thickness(0, 6, 0, 0);
            _confirm.Visibility = Visibility.Collapsed;
            _confirm.Tag = confirmText;

            Child = Ui.Column(0, _summary, actionsRow, _steerBox, _confirm, _transcript);
        }

        public void Update(AgentView agent)
        {
            _agent = agent;
            _summary.Text = Format.AgentSummary(agent, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var active = agent.Status == AgentStatus.Running || agent.Status == AgentStatus.Pending;
            _cancel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active) _confirm.Visibility = Visibility.Collapsed;
        }

        private void ShowSteer()
        {
            _steerBox.Visibility = Visibility.Visible;
            _steerInput.Focus();
        }

        private void ShowConfirm()
        {
            ((TextBlock)_confirm.Tag).Text = $"Cancel agent {_agent.Name} ({_agent.Id})?{(string.IsNullOrEmpty(_agent.Activity) ? "" : "\n" + _agent.Activity)}";
            _confirm.Visibility = Visibility.Visible;
        }

        private Task SteerAsync()
        {
            var message = _steerInput.Text.Trim();
            if (message.Length == 0) return Task.CompletedTask;
            var id = _agent.Id;
            return _actions.RunAsync("Steer agent", async () =>
            {
                await _actions.SteerAsync(id, message);
                _steerInput.Text = "";
                _steerBox.Visibility = Visibility.Collapsed;
            });
        }

        private Task CancelAsync()
        {
            var agent = _agent;
            _confirm.Visibility = Visibility.Collapsed;
            return _actions.RunAsync("Cancel agent", async () =>
            {
                var cancelled = await _actions.CancelAsync(agent.Id);
                if (!cancelled) _actions.Notice(NoticeLevel.Info, $"Agent {agent.Name} was not running.");
            });
        }

        private Task LoadTranscriptAsync()
        {
            var agent = _agent;
            _transcript.Content = Ui.Muted("Loading transcript…");
            return _actions.RunAsync("Open agent transcript", async () =>
            {
                try
                {
                    var items = await _actions.TranscriptAsync(agent.Id);
                    var list = new StackPanel();
                    TranscriptItem? previous = null;
                    foreach (var item in items)
                    {
                        var element = ItemRenderer.Render(item, _actions.RenderContext);
                        element.Margin = new Thickness(0, ItemRenderer.GapAbove(item, previous), 0, 0);
                        list.Children.Add(element);
                        previous = item;
                    }
                    if (items.Count == 0) list.Children.Add(Ui.Muted("The transcript is empty."));
                    var copy = Ui.Link("Copy as Markdown", () => _actions.RenderContext.Copy(Format.TranscriptToMarkdown($"{agent.Name} ({agent.Id})", items)));
                    copy.HorizontalAlignment = HorizontalAlignment.Left;
                    copy.Margin = new Thickness(0, 0, 0, 4);
                    var scroller = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                    var panel = Ui.Column(0, copy, scroller);
                    panel.Margin = new Thickness(0, 8, 0, 0);
                    _transcript.Content = panel;
                }
                catch
                {
                    _transcript.Content = null;
                    throw;
                }
            });
        }
    }

    /// <summary>Changes: one row per file changed by OMP (<c>M AuthService.cs  src\auth · +3 −1</c>); click for the native diff.</summary>
    internal sealed class ChangesSection : Section
    {
        private readonly Action<string> _openDiff;
        private readonly Action<string> _openFile;

        public ChangesSection(Action<string> openDiff, Action<string> openFile) : base("Changes")
        {
            _openDiff = openDiff;
            _openFile = openFile;
        }

        public void Render(IReadOnlyList<TrackedChange> changes, string? cwd)
        {
            SetTitle(ChangeRows.Title(changes.Count));
            Rows.Children.Clear();
            foreach (var change in changes) Rows.Children.Add(Row(change, cwd));
            if (changes.Count == 0) Rows.Children.Add(AgentsSection.EmptyRow("No files changed by OMP yet."));
        }

        private UIElement Row(TrackedChange change, string? cwd)
        {
            var letter = Ui.Text(ChangeRows.StatusLetter(change.Status),
                change.Status == ChangeStatus.Added ? ThemeKeys.Success : change.Status == ChangeStatus.Deleted ? ThemeKeys.Error : ThemeKeys.Progress,
                weight: FontWeights.SemiBold, small: true);
            letter.Width = 16;
            var name = Ui.Text(ChangeRows.FileName(change), small: true);
            name.Margin = new Thickness(0, 0, 8, 0);
            var detail = Ui.Muted(ChangeRows.Detail(change, cwd));
            var content = new DockPanel();
            DockPanel.SetDock(letter, Dock.Left);
            DockPanel.SetDock(name, Dock.Left);
            content.Children.Add(letter);
            content.Children.Add(name);
            content.Children.Add(detail);
            var counts = change.Status == ChangeStatus.Deleted ? "" : $" +{change.Added} −{change.Removed}";
            var tooltip = $"{change.Path}\n{ChangeRows.StatusText(change.Status)}{counts}\nClick for the diff";
            var row = new Button { Content = content, ToolTip = tooltip }.Styled("Omp.RowButton");
            Ui.AutomationName(row, $"Diff {ChangeRows.FileName(change)} {ChangeRows.StatusText(change.Status)}");
            var path = change.Path;
            row.Click += (_, __) => _openDiff(path);
            var open = Ui.IconButton(Glyphs.OpenFile, "Open file", () => _openFile(path));
            open.Width = 22;
            open.Height = 22;
            open.Visibility = change.Status == ChangeStatus.Deleted ? Visibility.Collapsed : Visibility.Visible;
            var line = new DockPanel();
            DockPanel.SetDock(open, Dock.Right);
            line.Children.Add(open);
            line.Children.Add(row);
            return line;
        }
    }
}
