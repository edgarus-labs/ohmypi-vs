using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>Error banner while OMP cannot be used (not found, not available, failed), with settings, log and restart actions.</summary>
    internal sealed class Banner : Border
    {
        private readonly TextBlock _text;

        public Banner(Action openSettings, Action showLog, Action restart)
        {
            Margin = new Thickness(8, 6, 8, 0);
            Padding = new Thickness(10, 6, 10, 6);
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            this.Theme(BorderBrushProperty, ThemeKeys.ErrorBorder);
            Visibility = Visibility.Collapsed;
            _text = Ui.Text("", ThemeKeys.Error, wrap: true);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(_text, System.Windows.Automation.AutomationLiveSetting.Assertive);
            var icon = Ui.Icon(Glyphs.Error, ThemeKeys.Error, 13);
            icon.Margin = new Thickness(0, 2, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (label, action) in new (string, Action)[] { ("Open Settings", openSettings), ("Show Log", showLog), ("Restart", restart) })
            {
                var button = Ui.Button(label, action);
                button.Margin = new Thickness(0, 0, 6, 0);
                actions.Children.Add(button);
            }
            var body = new StackPanel();
            body.Children.Add(_text);
            body.Children.Add(actions);
            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(body);
            Child = row;
        }

        public void Render(OmpUnavailable? unavailable, ConnectionStatus connection)
        {
            string? message = null;
            if (unavailable != null) message = $"{(unavailable.ExecutableNotFound ? "OMP not found" : "OMP is not available")}: {unavailable.Message}";
            else if (connection.State == ConnectionState.Failed) message = $"OMP stopped{(string.IsNullOrEmpty(connection.Detail) ? "" : $": {connection.Detail}")}";
            Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            ToolTip = message;
            if (_text.Text == (message ?? "")) return;
            _text.Text = message ?? "";
            if (message != null) Ui.Announce(_text);
        }
    }

    /// <summary>Centered welcome with recent sessions while the conversation is empty, or why OMP is not usable.</summary>
    internal sealed class WelcomeView : StackPanel
    {
        private readonly TextBlock _line;
        private readonly StackPanel _recentList = new StackPanel();
        private readonly StackPanel _recentBox;
        private readonly Button _viewAll;
        private readonly Button _start;
        private readonly Action<SessionSummary> _open;
        private RecentSessions _recent = new RecentSessions(Array.Empty<SessionSummary>(), 0);

        public WelcomeView(Action<SessionSummary> open, Action viewAll, Action start)
        {
            _open = open;
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Center;
            MaxWidth = 420;
            Margin = new Thickness(16);
            var mark = Ui.Text("π", ThemeKeys.Muted);
            mark.FontSize = 40;
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            _line = Ui.Text("", ThemeKeys.Foreground, wrap: true);
            _line.TextAlignment = TextAlignment.Center;
            _line.HorizontalAlignment = HorizontalAlignment.Center;
            _line.Margin = new Thickness(0, 6, 0, 0);
            _start = Ui.Button("Start OMP", start, "Omp.PrimaryButton");
            _start.HorizontalAlignment = HorizontalAlignment.Center;
            _start.Margin = new Thickness(0, 10, 0, 0);
            var heading = Ui.Muted("Recent");
            heading.Margin = new Thickness(6, 0, 0, 4);
            _viewAll = Ui.Link("", viewAll);
            _viewAll.Margin = new Thickness(6, 4, 0, 0);
            _viewAll.HorizontalAlignment = HorizontalAlignment.Left;
            _recentBox = Ui.Column(0, heading, _recentList, _viewAll);
            _recentBox.Margin = new Thickness(0, 18, 0, 0);
            _recentBox.MinWidth = 260;
            Children.Add(mark);
            Children.Add(_line);
            Children.Add(_start);
            Children.Add(_recentBox);
        }

        public bool HasRecent => _recent.Sessions.Count > 0;

        public void SetRecent(RecentSessions recent)
        {
            _recent = recent;
            RenderRecent();
        }

        public void Update(OmpUnavailable? unavailable, ConnectionStatus connection)
        {
            string line;
            var showRecent = false;
            if (unavailable != null) line = $"OMP is not available: {unavailable.Message}";
            else if (connection.State == ConnectionState.Starting || connection.State == ConnectionState.Restarting) line = "Starting OMP…";
            else if (connection.State == ConnectionState.Failed) line = $"OMP stopped{(string.IsNullOrEmpty(connection.Detail) ? "." : $": {connection.Detail}")}";
            else if (connection.State == ConnectionState.Stopped) line = "OMP is not running. Send a message or start it now.";
            else
            {
                line = "What should we build?";
                showRecent = true;
            }
            _line.Text = line;
            _start.Visibility = unavailable == null && connection.State == ConnectionState.Stopped ? Visibility.Visible : Visibility.Collapsed;
            _recentBox.Visibility = showRecent && HasRecent ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Re-renders the recent rows (their ages are relative to now).</summary>
        public void RenderRecent()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _recentList.Children.Clear();
            foreach (var session in _recent.Sessions)
            {
                var title = Format.SessionTitle(session);
                var age = Ui.Muted(Format.RelativeTime(session.Modified, now));
                age.Margin = new Thickness(12, 0, 0, 0);
                age.ToolTip = DateTimeOffset.FromUnixTimeMilliseconds(session.Modified).LocalDateTime.ToString(System.Globalization.CultureInfo.CurrentCulture);
                var row = new DockPanel();
                DockPanel.SetDock(age, Dock.Right);
                row.Children.Add(age);
                row.Children.Add(Ui.Text(title));
                var captured = session;
                var button = new Button { Content = row, ToolTip = title }.Styled("Omp.RowButton");
                Ui.AutomationName(button, $"Resume {title}");
                button.Click += (_, __) => _open(captured);
                _recentList.Children.Add(button);
            }
            var label = (TextBlock)_viewAll.Content;
            label.Text = $"View all ({_recent.Total})";
            _viewAll.Visibility = _recent.Total > _recent.Sessions.Count ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Session state above the composer: last error, queued messages and the todo list.</summary>
    internal sealed class SessionPanel : StackPanel
    {
        private static readonly Dictionary<string, (string Glyph, object Brush)> TodoIcons = new Dictionary<string, (string, object)>
        {
            ["completed"] = (Glyphs.Check, ThemeKeys.Success),
            ["in_progress"] = (Glyphs.CircleFill, ThemeKeys.Progress),
            ["pending"] = (Glyphs.Circle, ThemeKeys.Muted),
            ["abandoned"] = (Glyphs.Blocked, ThemeKeys.Muted),
            ["blocked"] = (Glyphs.Warning, ThemeKeys.Warning),
        };

        private readonly Border _statusLine;
        private readonly TextBlock _statusText;
        private readonly WrapPanel _queue = new WrapPanel();
        private readonly ContentControl _todos = new ContentControl { Focusable = false };
        private readonly RenderContext _ctx;
        private string? _queueShown;
        private string? _todosShown;
        /// <summary>The tasks the user closed the todo card on; it stays hidden until they change.</summary>
        private string? _todosDismissed;
        private readonly IDismissals? _dismissals;

        /// <param name="dismissals">Where a closed todo card is remembered across Visual Studio sessions; null to remember it only while this panel lives.</param>
        public SessionPanel(RenderContext ctx, IDismissals? dismissals)
        {
            _ctx = ctx;
            _dismissals = dismissals;
            _statusText = Ui.Text("", ThemeKeys.Error, small: true);
            var icon = Ui.Icon(Glyphs.Error, ThemeKeys.Error, 12);
            icon.Margin = new Thickness(0, 0, 6, 0);
            _statusLine = new Border
            {
                Child = Ui.Row(icon, _statusText),
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed,
            }.Theme(Border.BorderBrushProperty, ThemeKeys.ErrorBorder);
            _queue.Visibility = Visibility.Collapsed;
            _todos.Visibility = Visibility.Collapsed;
            Children.Add(_statusLine);
            Children.Add(_queue);
            Children.Add(_todos);
        }

        /// <summary>Updates the panel; the queue chips and the todo card are rebuilt only when their content changed.</summary>
        public void Render(SessionView session)
        {
            _statusLine.Visibility = string.IsNullOrEmpty(session.LastError) ? Visibility.Collapsed : Visibility.Visible;
            _statusText.Text = session.LastError ?? "";
            _statusLine.ToolTip = session.LastError;
            var queue = string.Join("\u0001", session.Queue.Steering) + "\u0002" + string.Join("\u0001", session.Queue.FollowUp);
            if (queue != _queueShown)
            {
                _queueShown = queue;
                RenderQueue(session.Queue);
            }
            var todos = string.Join("\u0001", session.Todos.Select(phase => phase.Name + "\u0002" + string.Join("\u0003", phase.Tasks.Select(task => task.Status + "\u0004" + task.Content))));
            if (todos != _todosShown)
            {
                _todosShown = todos;
                RenderTodos(session.Todos);
            }
        }

        private void RenderQueue(QueueView queue)
        {
            _queue.Children.Clear();
            foreach (var text in queue.Steering) _queue.Children.Add(Chip("steer", text));
            foreach (var text in queue.FollowUp) _queue.Children.Add(Chip("follow-up", text));
            _queue.Visibility = _queue.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            _queue.Margin = new Thickness(0, 0, 0, 6);
        }

        private static UIElement Chip(string kind, string text)
        {
            var label = Ui.Muted(kind);
            label.Margin = new Thickness(0, 0, 6, 0);
            var body = Ui.Text(text.Replace('\n', ' '), small: true);
            body.MaxWidth = 280;
            var chip = Ui.Card(Ui.Row(label, body), new Thickness(8, 1, 8, 1));
            chip.Margin = new Thickness(0, 0, 4, 4);
            chip.ToolTip = text;
            Ui.AutomationName(chip, $"Queued {kind}: {text}");
            return chip;
        }

        /// <summary>Shows the todo card; its close button hides it until the tasks change, also after Visual Studio restarts.</summary>
        private void RenderTodos(IReadOnlyList<TodoPhaseView> phases)
        {
            var summary = Chrome.TodoSummary(phases);
            if (summary == null || _todosDismissed == _todosShown || (_dismissals?.IsDismissed(TodosKey(_todosShown)) ?? false))
            {
                _todos.Content = null;
                _todos.Visibility = Visibility.Collapsed;
                return;
            }
            var header = Ui.Text(summary, small: true);
            header.ToolTip = summary;
            var section = Collapsible.Create(_ctx, "todos", header, () => TodoList(phases), bodyMargin: new Thickness(16, 2, 0, 4));
            var close = Ui.IconButton("\uE711", "Close the todo list", () =>
            {
                _todosDismissed = _todosShown;
                try
                {
                    _dismissals?.Dismiss(TodosKey(_todosShown));
                }
                catch (Exception error)
                {
                    _ctx.LogError("Remembering the closed todo list failed", error);
                }
                RenderTodos(phases);
            });
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            var card = new Grid { Children = { section, close } };
            _todos.Content = Ui.Card(card, new Thickness(4, 2, 8, 2));
            _todos.Visibility = Visibility.Visible;
            _todos.Margin = new Thickness(0, 0, 0, 6);
        }

        /// <summary>A short, stable key for a todo list's content: "todos:" and the SHA-1 of its text.</summary>
        private static string TodosKey(string? todos)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(todos ?? ""));
                return "todos:" + BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private static UIElement TodoList(IReadOnlyList<TodoPhaseView> phases)
        {
            var list = new StackPanel();
            foreach (var phase in phases)
            {
                var name = Ui.Muted(phase.Name);
                name.FontWeight = FontWeights.SemiBold;
                name.Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 4, 0, 2);
                list.Children.Add(name);
                foreach (var task in phase.Tasks)
                {
                    var (glyph, brush) = TodoIcons.TryGetValue(task.Status, out var icon) ? icon : (Glyphs.Circle, ThemeKeys.Muted);
                    var mark = Ui.Icon(glyph, brush, 11);
                    mark.Margin = new Thickness(0, 2, 6, 0);
                    mark.VerticalAlignment = VerticalAlignment.Top;
                    var done = task.Status == "completed" || task.Status == "abandoned";
                    var text = Ui.Text(task.Content, done ? ThemeKeys.Muted : null, wrap: true, small: true);
                    if (task.Status == "abandoned") text.TextDecorations = TextDecorations.Strikethrough;
                    if (task.Status == "in_progress") text.FontWeight = FontWeights.SemiBold;
                    var row = new DockPanel { ToolTip = task.Status.Replace('_', ' ') };
                    DockPanel.SetDock(mark, Dock.Left);
                    row.Children.Add(mark);
                    row.Children.Add(text);
                    list.Children.Add(row);
                }
            }
            var scroller = new ScrollViewer { Content = list, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            return scroller;
        }
    }
}
