using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

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

    /// <summary>
    /// Initializes a new instance of the WelcomeView class with the specified actions for opening session summaries, viewing all records, and starting the application.
    /// </summary>
    /// <param name="open">The open.</param>
    /// <param name="viewAll">The view all.</param>
    /// <param name="start">The start.</param>
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

    /// <summary>
    /// Gets a value indicating whether has recent.
    /// </summary>
    public bool HasRecent => _recent.Sessions.Count > 0;

    /// <summary>
    /// Updates the recent sessions collection and triggers a re-render of the recent sessions display.
    /// </summary>
    /// <param name="recent">The recent.</param>
    public void SetRecent(RecentSessions recent)
    {
        _recent = recent;
        RenderRecent();
    }

    /// <summary>
    /// Updates the user interface elements to reflect the current OMP availability and connection status.
    /// </summary>
    /// <param name="unavailable">The unavailable.</param>
    /// <param name="connection">The connection.</param>
    public void Update(OmpUnavailable? unavailable, ConnectionStatus connection)
    {
        string line;
        var showRecent = false;
        if (unavailable is not null)
        {
            line = $"OMP is not available: {unavailable.Message}";
        }
        else if (connection.State == ConnectionState.Starting || connection.State == ConnectionState.Restarting)
        {
            line = "Starting OMP…";
        }
        else if (connection.State == ConnectionState.Failed)
        {
            line = $"OMP stopped{(string.IsNullOrEmpty(connection.Detail) ? "." : $": {connection.Detail}")}";
        }
        else if (connection.State == ConnectionState.Stopped)
        {
            line = "OMP is not running. Send a message or start it now.";
        }
        else
        {
            line = "What should we build?";
            showRecent = true;
        }
        _line.Text = line;
        _start.Visibility = unavailable is null && connection.State == ConnectionState.Stopped ? Visibility.Visible : Visibility.Collapsed;
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
