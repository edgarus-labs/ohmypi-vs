using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

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

    /// <summary>
    /// Populates the queue UI container with steering and follow-up chips based on the provided queue view and updates its visibility and layout properties.
    /// </summary>
    /// <param name="queue">The queue.</param>
    private void RenderQueue(QueueView queue)
    {
        _queue.Children.Clear();
        foreach (var text in queue.Steering)
        {
            _queue.Children.Add(Chip("steer", text));
        }

        foreach (var text in queue.FollowUp)
        {
            _queue.Children.Add(Chip("follow-up", text));
        }

        _queue.Visibility = _queue.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _queue.Margin = new Thickness(0, 0, 0, 6);
    }

    /// <summary>
    /// Creates a styled UI chip element consisting of a muted category label and a truncated text body with associated automation properties.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text.</param>
    /// <returns>The uielement result.</returns>
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
        if (summary is null || _todosDismissed == _todosShown || (_dismissals?.IsDismissed(TodosKey(_todosShown)) ?? false))
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

    /// <summary>
    /// Constructs a scrollable UI element that visually represents a hierarchical list of todo phases and their associated tasks, applying specific styling based on task status.
    /// </summary>
    /// <param name="phases">The collection of phases.</param>
    /// <returns>The uielement result.</returns>
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
                if (task.Status == "abandoned")
                {
                    text.TextDecorations = TextDecorations.Strikethrough;
                }

                if (task.Status == "in_progress")
                {
                    text.FontWeight = FontWeights.SemiBold;
                }

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
