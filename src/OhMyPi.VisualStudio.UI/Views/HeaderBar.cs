using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Thin header: session name (click to rename inline), activity, the model, the state indicator with the elapsed time of the
/// current turn, context and cost. New Session and Session History live on the tool window's toolbar.
/// </summary>
internal sealed class HeaderBar : Border
{
    private readonly Button _sessionButton;
    private readonly TextBlock _sessionText;
    private readonly TextBox _renameBox;
    private readonly TextBlock _activity;
    private readonly Border _statePill;
    private readonly Ellipse _dot;
    private readonly TextBlock _stateWord;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _usage;
    private readonly TextBlock _model;
    private readonly Button _resumeRouter;
    private readonly DispatcherTimer _timer;
    private DateTime? _busySince;
    private StateWord _word = StateWord.Offline;
    private bool _compacting;
    private IReadOnlyList<string> _statusTexts = Array.Empty<string>();

    public HeaderBar()
    {
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Theme(BorderBrushProperty, ThemeKeys.Divider);
        Padding = new Thickness(8, 3, 4, 3);

        _sessionText = Ui.Text("New chat", weight: FontWeights.SemiBold);
        _sessionButton = new Button { Content = _sessionText, HorizontalContentAlignment = HorizontalAlignment.Left }.Styled("Omp.Button");
        _sessionButton.Click += (_, __) => BeginRename();
        _renameBox = new TextBox { Visibility = Visibility.Collapsed, MinWidth = 120 }.Styled("Omp.TextBox");
        Ui.AutomationName(_renameBox, "Session name");
        _renameBox.KeyDown += OnRenameKey;
        _renameBox.LostKeyboardFocus += (_, __) => EndRename(commit: false, restoreFocus: false);

        _activity = Ui.Muted("");
        _activity.Margin = new Thickness(8, 0, 0, 0);

        _dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        _stateWord = Ui.Text("", ThemeKeys.Muted, small: true);
        _stateWord.Margin = new Thickness(6, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_stateWord, System.Windows.Automation.AutomationLiveSetting.Polite);
        _elapsed = Ui.Text("", ThemeKeys.Muted, small: true);
        _elapsed.Margin = new Thickness(6, 0, 0, 0);
        _statePill = new Border
        {
            Child = Ui.Row(_dot, _stateWord, _elapsed),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        }.Styled("Omp.Card");

        _usage = Ui.Muted("");
        _usage.Margin = new Thickness(8, 0, 4, 0);

        _model = Ui.Muted("");
        _model.Margin = new Thickness(8, 0, 0, 0);
        _model.MaxWidth = 160;
        _model.TextTrimming = TextTrimming.CharacterEllipsis;
        _model.VerticalAlignment = VerticalAlignment.Center;

        _resumeRouter = Ui.Button(Ui.Text("Resume auto", small: true), () => ResumeRouterRequested?.Invoke(), tooltip: "The model was set manually, so the tier router is paused. Sends /tier-auto.");
        _resumeRouter.Margin = new Thickness(6, 0, 0, 0);
        _resumeRouter.VerticalAlignment = VerticalAlignment.Center;
        _resumeRouter.Visibility = Visibility.Collapsed;
        Ui.AutomationName(_resumeRouter, "Resume tier router auto routing");

        var right = Ui.Row(_model, _resumeRouter, _statePill, _usage);
        var left = new DockPanel { LastChildFill = true };
        var title = new Grid();
        title.Children.Add(_sessionButton);
        title.Children.Add(_renameBox);
        DockPanel.SetDock(title, Dock.Left);
        left.Children.Add(title);
        left.Children.Add(_activity);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);
        root.Children.Add(left);
        Child = root;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, __) => RenderActivity();
    }

    public event Action<string>? Renamed;

    public event Action? ResumeRouterRequested;

    public void Render(SessionView session, ConnectionStatus connection, int pendingInteractions, bool unavailable)
    {
        _word = Chrome.GetStateWord(connection.State, session.Phase, pendingInteractions, unavailable);
        TrackBusy(session.Phase != SessionPhase.Idle);
        _compacting = session.IsCompacting;
        var name = string.IsNullOrEmpty(session.SessionName) ? "New chat" : session.SessionName!;
        _sessionText.Text = name;
        _sessionButton.ToolTip = $"{name}\n{session.SessionFile ?? "No session file yet"}\nClick to rename";
        Ui.AutomationName(_sessionButton, $"Session {name}, rename");

        _statePill.Visibility = _word == StateWord.Idle ? Visibility.Collapsed : Visibility.Visible;
        var word = _word.ToString().ToLowerInvariant();
        if (_stateWord.Text != word)
        {
            _stateWord.Text = word;
            Ui.Announce(_stateWord);
        }
        _dot.SetResourceReference(Shape.FillProperty,
            _word == StateWord.Offline ? ThemeKeys.Error
            : _word == StateWord.Waiting || _word == StateWord.Aborting ? ThemeKeys.Warning
            : ThemeKeys.Progress);
        _statePill.ToolTip = $"connection: {connection.State.ToString().ToLowerInvariant()}{(string.IsNullOrEmpty(connection.Detail) ? "" : $" ({connection.Detail})")}";

        _model.Text = Chrome.HeaderModelText(session.Model);
        _model.ToolTip = session.Model is null ? null : $"{session.Model.Provider}/{session.Model.Id}";
        _model.Visibility = _model.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var usage = Chrome.UsageText(session.ContextUsage, session.CostUsd);
        _usage.Text = usage;
        _usage.Visibility = usage.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (session.ContextUsage is not null && session.ContextUsage.Percent >= 80)
        {
            _usage.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Warning);
        }
        else
        {
            _usage.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Muted);
        }

        _usage.ToolTip = session.ContextUsage is null
            ? (usage.Length == 0 ? null : usage)
            : $"{usage}\n{session.ContextUsage.Tokens.ToString("N0", CultureInfo.CurrentCulture)} / {session.ContextUsage.ContextWindow.ToString("N0", CultureInfo.CurrentCulture)} context tokens";
        RenderActivity();
    }

    /// <summary>Status texts set by OMP extensions (<c>setStatus</c>), shown next to the session name.</summary>
    public void SetStatusTexts(IReadOnlyList<string> texts)
    {
        _statusTexts = texts;
        RenderActivity();
    }

    /// <summary>Shows the action that resumes the tier router while it is paused by a manual model change.</summary>
    public void SetRouterPaused(bool paused) => _resumeRouter.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;

    public void BeginRename()
    {
        _renameBox.Text = _sessionText.Text == "New chat" ? "" : _sessionText.Text;
        _sessionButton.Visibility = Visibility.Collapsed;
        _renameBox.Visibility = Visibility.Visible;
        _renameBox.Focus();
        _renameBox.SelectAll();
    }

    public void StopTimer() => _timer.Stop();

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            EndRename(commit: true, restoreFocus: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndRename(commit: false, restoreFocus: true);
        }
    }

    /// <summary>Leaves rename mode; focus returns to the session button only when the user ended it with Enter or Escape.</summary>
    private void EndRename(bool commit, bool restoreFocus)
    {
        if (_renameBox.Visibility != Visibility.Visible)
        {
            return;
        }

        var name = _renameBox.Text.Trim();
        _renameBox.Visibility = Visibility.Collapsed;
        _sessionButton.Visibility = Visibility.Visible;
        if (commit && name.Length > 0)
        {
            _sessionText.Text = name;
            Renamed?.Invoke(name);
        }
        if (restoreFocus)
        {
            _sessionButton.Focus();
        }
    }

    private void RenderActivity()
    {
        _elapsed.Text = _busySince.HasValue && _word != StateWord.Offline && _word != StateWord.Starting
            ? Chrome.ElapsedText((long)(DateTime.UtcNow - _busySince.Value).TotalMilliseconds)
            : "";
        _elapsed.Visibility = _elapsed.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var parts = new List<string>();
        if (_compacting)
        {
            parts.Add("compacting…");
        }

        parts.AddRange(_statusTexts);
        _activity.Text = string.Join(" · ", parts);
        _activity.ToolTip = _activity.Text.Length == 0 ? null : _activity.Text;
    }

    private void TrackBusy(bool busy)
    {
        if (!busy)
        {
            _busySince = null;
            _timer.Stop();

            return;
        }
        if (!_busySince.HasValue)
        {
            _busySince = DateTime.UtcNow;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }
}
