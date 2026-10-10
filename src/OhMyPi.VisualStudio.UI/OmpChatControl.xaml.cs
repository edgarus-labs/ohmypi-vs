using OhMyPi.VisualStudio.UI.Model;
using OhMyPi.VisualStudio.UI.Views;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI;

/// <summary>
/// The oh-my-pi chat: header, Agents and Changes sections, the transcript, pending questions, queue and todos,
/// and the composer. Service events arrive on background threads; they are coalesced and applied on this
/// control's dispatcher at most once per frame-sized interval, so long streams never block Visual Studio.
/// <see cref="Dispose"/> detaches the control from its service and host and stops its timers.
/// </summary>
public sealed partial class OmpChatControl : UserControl, IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(33);

    private readonly TranscriptList _transcript = new TranscriptList();
    private readonly OpenState _open = new OpenState();
    private readonly UpdateQueue _queue = new UpdateQueue();
    private readonly FollowBottom _follow = new FollowBottom();
    private ActivityItem? _activity;
    private readonly Dictionary<string, string> _statusTexts = new Dictionary<string, string>(StringComparer.Ordinal);
    private bool _routerPaused;
    private bool _routerRouting;
    private readonly DispatcherTimer _flushTimer;
    private readonly DispatcherTimer _recentTimer;
    private readonly List<Action> _detach = new List<Action>();
    private bool _disposed;

    private IOmpService? _service;
    private IOmpHost? _host;
    private RenderContext? _ctx;
    private HeaderBar? _header;
    private Banner? _banner;
    private WelcomeView? _welcome;
    private AgentsSection? _agents;
    private ChangesSection? _changes;
    private UsagePopup? _usagePopup;
    private DateTime _usageClosedAt;
    private SessionPanel? _sessionPanel;
    private InteractionsView? _interactions;
    private Composer? _composer;
    private Button? _jump;
    private ModelPicker? _modelPicker;
    private PickerPopup? _effortPicker;
    private PickerList? _sessionPicker;
    private SessionView _session = new SessionView();
    private ConnectionStatus _connection = new ConnectionStatus { State = ConnectionState.Stopped };
    private string? _recentFor;
    private int _recentListing;

    public OmpChatControl()
    {
        InitializeComponent();
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = FlushInterval };
        _flushTimer.Tick += (_, __) =>
        {
            _flushTimer.Stop();
            if (_disposed)
            {
                return;
            }

            try
            {
                Flush();
            }
            catch (Exception error)
            {
                Fail("Updating the chat failed", error);
            }
        };
        _recentTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMinutes(1) };
        _recentTimer.Tick += (_, __) =>
        {
            if (_welcome?.Visibility == Visibility.Visible && _welcome.HasRecent)
            {
                _welcome.RenderRecent();
            }
        };
        SizeChanged += (_, __) => ApplyHeights();
        Transcript.SetResourceReference(ChatFontSizeProperty, ThemeKeys.ChatFontSize);
        ZoomTransform = new ScaleTransform(s_zoom, s_zoom);
        ((FrameworkElement)Content).LayoutTransform = ZoomTransform;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    /// <summary>The conversation's text size; text sized relative to the base size inside the conversation scales from it too.</summary>
    private static readonly DependencyProperty ChatFontSizeProperty =
        DependencyProperty.RegisterAttached("ChatFontSize", typeof(double), typeof(OmpChatControl), new PropertyMetadata(double.NaN, OnChatFontSizeChanged));

    private static void OnChatFontSizeChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (!(element is Control transcript) || !(e.NewValue is double size) || double.IsNaN(size))
        {
            return;
        }

        transcript.FontSize = size;
        transcript.Resources[ThemeKeys.FontSize] = size;
    }

    /// <summary>Zoom level shared by every chat control in this VS process, so a recreated window keeps it.</summary>
    private static double s_zoom = 1.0;

    /// <summary>Scale of the control's content; popups owned by the control apply the same transform.</summary>
    internal ScaleTransform ZoomTransform { get; }

    /// <summary>Opens a confirmed, normalized web URL; the default hands it to the shell's browser.</summary>
    internal Action<string> LaunchUrl { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>Puts text on the clipboard; the default is the system clipboard.</summary>
    internal Action<string> SetClipboard { get; set; } = Clipboard.SetText;

    /// <summary>The keyboard modifiers held down now; the default reads the keyboard.</summary>
    internal Func<ModifierKeys> Modifiers { get; set; } = () => Keyboard.Modifiers;

    /// <summary>How often the ages of the recent sessions on the welcome screen are refreshed.</summary>
    internal TimeSpan RecentRefreshInterval
    {
        get => _recentTimer.Interval;
        set => _recentTimer.Interval = value;
    }

    /// <summary>Unsubscribes from the service and host and stops every timer, so a replaced control can be collected.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var detach in _detach)
        {
            detach();
        }

        _detach.Clear();
        _flushTimer.Stop();
        _recentTimer.Stop();
        _header?.StopTimer();
        _interactions?.StopTimer();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Modifiers() & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        s_zoom = Zoom.Step(ZoomTransform.ScaleX, e.Delta);
        ZoomTransform.ScaleX = s_zoom;
        ZoomTransform.ScaleY = s_zoom;
        ApplyHeights();
    }

    /// <summary>Binds the control to a service and its host. Call once, on the UI thread.</summary>
    public void Initialize(IOmpService service, IOmpHost host)
    {
        if (service is null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        if (host is null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        if (_service is not null)
        {
            throw new InvalidOperationException("OmpChatControl is already initialized.");
        }

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OmpChatControl));
        }

        _service = service;
        _host = host;
        BuildViews(service, host);
        Subscribe(service, host);

        _composer!.Commands = service.Commands;
        foreach (var status in service.Statuses)
        {
            ApplyStatus(status.Key, status.Value);
        }

        RenderStatuses();
        _connection = service.Connection;
        _session = service.Session;
        _transcript.Reset(service.Transcript);
        _agents!.Render(service.Agents);
        _ctx!.Changes = host.Changes;
        _changes!.Render(_ctx.Changes, service.Cwd);
        foreach (var request in service.PendingInteractions)
        {
            _interactions!.Add(request);
        }

        RenderSession();
        _recentTimer.Start();
        ScrollToBottom();
    }

    public void FocusInput()
    {
        if (_composer is null)
        {
            return;
        }

        _composer.Input.Focus();
        System.Windows.Input.Keyboard.Focus(_composer.Input);
    }

    public void ShowModelPicker() => _ = OpenModelPickerAsync();

    public void ShowThinkingPicker() => OpenEffortPicker();

    public void ShowSessionPicker() => _ = OpenSessionPickerAsync();

    public void BeginRename() => _header?.BeginRename();

    public void ShowAgents() => _agents?.Expand();

    /// <summary>
    /// Opens the usage popup, or closes it when it is open. A click on the toolbar button closes the popup before the
    /// command runs, so a popup closed a moment ago stays closed.
    /// </summary>
    public void ShowUsage() => _ = Dispatcher.InvokeAsync(() =>
    {
        if (_usagePopup is null)
        {
            return;
        }

        if (_usagePopup.Popup.IsOpen)
        {
            _usagePopup.Close();
        }
        else if (DateTime.UtcNow - _usageClosedAt > TimeSpan.FromMilliseconds(400))
        {
            _usagePopup.Open();
        }
    });

    /// <summary>Adds files or folders as chips above the input; OMP reads them through <c>@path</c> mentions on send.</summary>
    public void AddFileMentions(IReadOnlyList<string> paths)
    {
        if (_composer is null || paths is null || paths.Count == 0)
        {
            return;
        }

        _composer.AddAttachments(paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => (Attachment)new FileAttachment(p)).ToList());
        FocusInput();
    }

    private void BuildViews(IOmpService service, IOmpHost host)
    {
        _ctx = new RenderContext(_open, OpenFile, OpenDiff, ShowAgents, OpenUrl, Copy, host.LogError) { Cwd = service.Cwd };

        _header = new HeaderBar();
        _header.Renamed += name => _ = RunAsync("Rename session", WithService(() => service.SetSessionNameAsync(name)));
        _header.ResumeRouterRequested += () => _ = RunAsync("Resume tier router", WithService(async () => await service.PromptAsync("/tier-auto")));
        HeaderHost.Content = _header;

        _banner = new Banner(host.OpenSettings, host.ShowLog, () => _ = RunAsync("Restart", host.RestartAsync));
        BannerHost.Content = _banner;

        _agents = new AgentsSection(new AgentActions(this));
        _changes = new ChangesSection(path => OpenDiff(path, null), path => OpenFile(path, null));
        SectionsHost.Children.Add(_agents);
        SectionsHost.Children.Add(_changes);

        Transcript.Render = item => ItemRenderer.Render(item, _ctx);
        Transcript.Previous = _transcript.Previous;
        Transcript.ItemsSource = _transcript.Items;
        Transcript.ScrollerReady += (_, __) => HookScroller();
        HookScroller();

        _welcome = new WelcomeView(
            summary => _ = RunAsync("Open session", () => SwitchSessionAsync(summary.Path)),
            ShowSessionPicker,
            () => _ = RunAsync("Start OMP", host.RestartAsync));
        WelcomeHost.Content = _welcome;

        _jump = Ui.Button(Ui.Icon(Glyphs.ArrowDown, null, 12), ScrollToBottom, tooltip: "Scroll to the latest output");
        Ui.AutomationName(_jump, "Jump to latest");
        _jump.Visibility = Visibility.Collapsed;
        JumpHost.Content = _jump;

        _sessionPanel = new SessionPanel(_ctx, host as IDismissals);
        SessionPanelHost.Content = _sessionPanel;

        _interactions = new InteractionsView(service.RespondInteraction, OpenUrl, error => Fail("Answering OMP failed", error), FocusInput);
        _interactions.Changed += RenderSession;
        InteractionsScroller.Content = _interactions;

        _composer = new Composer(AddNotice, (message, error) => host.LogError(message, error)) { ActiveDocument = host.ActiveDocumentPath };
        _composer.Submitted += draft => _ = SendAsync(draft);
        _composer.StopRequested += () => _ = RunAsync("Abort", service.AbortAsync);
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _session.Phase == SessionPhase.Idle)
            {
                return;
            }

            e.Handled = true;
            _ = RunAsync("Abort", service.AbortAsync);
        };
        _composer.AttachRequested += () =>
        {
            var path = host.ActiveDocumentPath;
            if (path is not null)
            {
                AddFileMentions(new[] { path });
            }
        };
        _composer.ModelRequested += ShowModelPicker;
        _composer.EffortRequested += ShowThinkingPicker;
        _composer.FastRequested += enabled => _ = RunAsync("Fast mode", WithService(() => service.SetFastModeAsync(enabled)));
        ComposerHost.Content = _composer;

        _modelPicker = new ModelPicker(_composer.ModelButton, TranscriptArea, ComposerHost, host.ModelPreferences, Copy, Fail);
        _modelPicker.Picked += model => _ = RunAsync("Select model", WithService(() => service.SetModelAsync(model.Provider, model.Id)));
        _modelPicker.Popup.Opened += (_, __) => SetTranscriptScrollBar(ScrollBarVisibility.Hidden);
        _modelPicker.Popup.Closed += (_, __) => SetTranscriptScrollBar(ScrollBarVisibility.Auto);
        _usagePopup = new UsagePopup(TranscriptArea, ComposerHost, () => service.GetUsageAsync(), (message, error) => host.LogError(message, error), () =>
        {
            _usageClosedAt = DateTime.UtcNow;
            FocusInput();
        });
        _effortPicker = new PickerPopup(_composer.EffortButton, "Reasoning effort", searchable: false, width: 280);
        _effortPicker.Picked += value => _ = RunAsync("Set reasoning effort", WithService(() => service.SetThinkingLevelAsync((string)value)));
        _sessionPicker = new PickerList("Session history", searchable: true, searchName: "Search sessions");
        _sessionPicker.Picked += value =>
        {
            CloseSessionHistory();
            _ = RunAsync("Open session", () => SwitchSessionAsync(((SessionSummary)value).Path));
        };
        _sessionPicker.Dismissed += CloseSessionHistory;
        _sessionPicker.Heading.Visibility = Visibility.Collapsed;
        _sessionPicker.List.Margin = new Thickness(4, 0, 4, 0);
        var close = Ui.IconButton(Glyphs.Cancel, "Close session history", CloseSessionHistory);
        DockPanel.SetDock(close, Dock.Right);
        var title = Ui.Muted("Session history");
        title.Margin = new Thickness(8, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        var historyHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        historyHeader.Children.Add(close);
        historyHeader.Children.Add(title);
        DockPanel.SetDock(historyHeader, Dock.Top);
        var historyLayout = new DockPanel { Margin = new Thickness(8, 2, 8, 6) };
        historyLayout.Children.Add(historyHeader);
        historyLayout.Children.Add(_sessionPicker);
        var historyCard = new Border { Child = historyLayout };
        historyCard.Theme(BackgroundProperty, ThemeKeys.Background);
        Ui.AutomationName(historyCard, "Session history");
        HistoryHost.Content = historyCard;
        ApplyHeights();
    }

    private void Subscribe(IOmpService service, IOmpHost host)
    {
        On<ConnectionStatus>(h => service.ConnectionChanged += h, h => service.ConnectionChanged -= h, (_, connection) => Schedule(_queue.EnqueueConnection(connection)));
        On<SessionView>(h => service.SessionChanged += h, h => service.SessionChanged -= h, (_, session) => Schedule(_queue.EnqueueSession(session)));
        On<TranscriptItem>(h => service.TranscriptItemChanged += h, h => service.TranscriptItemChanged -= h, (_, item) => Schedule(_queue.EnqueueItem(item)));
        On<IReadOnlyList<TranscriptItem>>(h => service.TranscriptReset += h, h => service.TranscriptReset -= h, (_, items) => Schedule(_queue.EnqueueReset(items)));
        On<IReadOnlyList<AgentView>>(h => service.AgentsChanged += h, h => service.AgentsChanged -= h, (_, agents) => Schedule(_queue.EnqueueAgents(agents)));
        On<InteractionRequest>(h => service.InteractionRequested += h, h => service.InteractionRequested -= h, (_, request) => Post(() => _interactions!.Add(request)));
        On<string>(h => service.InteractionCancelled += h, h => service.InteractionCancelled -= h, (_, id) => Post(() => _interactions!.Remove(id)));
        On<PresentationRequest>(h => service.Presentation += h, h => service.Presentation -= h, (_, request) => Post(() => Present(request)));
        On<IReadOnlyList<SlashCommandView>>(h => service.CommandsChanged += h, h => service.CommandsChanged -= h, (_, commands) => Post(() => _composer!.Commands = commands));
        On(h => host.ChangesChanged += h, h => host.ChangesChanged -= h, (_, __) => Schedule(_queue.EnqueueChanges()));
        On(h => host.ActiveDocumentChanged += h, h => host.ActiveDocumentChanged -= h, (_, __) => Post(() => _composer!.ActiveDocument = host.ActiveDocumentPath));
    }

    private void On<T>(Action<EventHandler<T>> add, Action<EventHandler<T>> remove, EventHandler<T> handler)
    {
        add(handler);
        _detach.Add(() => remove(handler));
    }

    private void On(Action<EventHandler> add, Action<EventHandler> remove, EventHandler handler)
    {
        add(handler);
        _detach.Add(() => remove(handler));
    }

    /// <summary>Called on any thread: starts the flush timer when the first update of a batch arrives.</summary>
    private void Schedule(bool firstPending)
    {
        if (!firstPending)
        {
            return;
        }

        Post(() =>
        {
            if (!_flushTimer.IsEnabled)
            {
                _flushTimer.Start();
            }
        });
    }

    private void Post(Action action) => _ = Dispatcher.InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception error)
        {
            Fail("Updating the chat failed", error);
        }
    });

    private void Flush()
    {
        var batch = _queue.Drain();
        if (batch.Connection is not null)
        {
            _connection = batch.Connection;
        }

        if (batch.ChangesChanged)
        {
            _ctx!.Changes = _host!.Changes;
            _ctx.ForgetMissingFiles();
        }
        if (batch.Reset is not null)
        {
            _open.Clear();
            _transcript.Reset(batch.Reset);
            ScrollToBottom();
        }
        if (batch.Session is not null)
        {
            _session = batch.Session;
        }

        foreach (var item in batch.Items)
        {
            _transcript.Upsert(item);
        }

        if (batch.Agents is not null)
        {
            _agents!.Render(batch.Agents);
        }

        if (batch.ChangesChanged)
        {
            _changes!.Render(_ctx!.Changes, _service!.Cwd);
        }

        if (batch.Connection is not null || batch.Session is not null || batch.Reset is not null || batch.Items.Count > 0)
        {
            RenderSession();
        }

        if (batch.Session is not null || batch.Reset is not null)
        {
            RenderActivity();
        }
    }

    /// <summary>Keeps the activity row in step with the prompt lifecycle; it starts timing when work begins.</summary>
    private void RenderActivity()
    {
        var label = ActivityText.Label(_session);
        if (label is null)
        {
            _activity = null;
            _transcript.SetActivity(null);

            return;
        }
        if (_activity is not null && _activity.Label == label)
        {
            return;
        }

        var started = _activity?.StartedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _activity = new ActivityItem(label, started);
        _transcript.SetActivity(_activity);
    }

    private OmpUnavailable? Unavailable => _host?.Unavailable;

    private void RenderSession()
    {
        if (_service is null)
        {
            return;
        }

        var unavailable = Unavailable;
        _header!.Render(_session, _connection, _interactions!.PendingCount, unavailable is not null);
        _banner!.Render(unavailable, _connection);
        _composer!.RenderSession(_session);
        _composer.SetStatus(_session.Phase != SessionPhase.Idle, unavailable is not null);
        _sessionPanel!.Render(_session);
        _ctx!.Cwd = _service.Cwd;
        var empty = !_transcript.HasConversation;
        _welcome!.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _welcome.Update(unavailable, _connection);
        if (_connection.State == ConnectionState.Ready && _session.SessionFile != _recentFor)
        {
            _ = RefreshRecentAsync();
        }
    }

    private async Task RefreshRecentAsync()
    {
        var service = _service!;
        var listing = ++_recentListing;
        _recentFor = _session.SessionFile;
        try
        {
            var sessions = await service.ListSessionsAsync();
            if (listing != _recentListing || _disposed)
            {
                return;
            }

            _welcome!.SetRecent(SessionList.Recent(sessions, _recentFor));
            _welcome.Update(Unavailable, _connection);
        }
        catch (Exception error) when (!(error is OmpSupersededException))
        {
            if (listing != _recentListing || _disposed)
            {
                return;
            }

            _host!.LogError("Listing recent sessions failed", error);
            AddNotice(NoticeLevel.Warning, $"Listing recent sessions failed: {error.Message}");
        }
    }

    private void ApplyStatus(string key, string? text)
    {
        if (key == "tier")
        {
            _routerPaused = !string.IsNullOrEmpty(text) && Chrome.IsRouterPaused(text!);
            _routerRouting = !string.IsNullOrEmpty(text) && Chrome.IsRouterRouting(text!);
        }

        if (string.IsNullOrEmpty(text))
        {
            _statusTexts.Remove(key);
        }
        else
        {
            _statusTexts[key] = key == "tier" ? Chrome.RouterStatusText(text!) : text!;
        }
    }

    private void RenderStatuses()
    {
        _header!.SetStatusTexts(_statusTexts.Values.ToList());
        _header.SetRouterPaused(_routerPaused);
        _composer!.SetRouterAuto(_routerRouting);
    }

    private void Present(PresentationRequest request)
    {
        switch (request)
        {
            case NotifyPresentation notify:
                AddNotice(notify.Level, notify.Message);
                break;

            case StatusPresentation status:
                ApplyStatus(status.Key, status.Text);
                RenderStatuses();
                break;

            case OpenUrlPresentation openUrl:
                if (MarkdownView.IsWebUrl(openUrl.Url))
                {
                    _interactions!.AddOpenUrl(openUrl.Url, openUrl.Instructions);
                }
                else
                {
                    AddNotice(NoticeLevel.Warning, $"OMP asked to open a non-web URL; ignored: {openUrl.Url}");
                }

                break;

            case EditorTextPresentation editorText:
                _composer!.SetText(editorText.Text);
                break;
        }
    }

    private void AddNotice(NoticeLevel level, string text)
    {
        _transcript.AddNotice(level, text);
        RenderSession();
    }

    /// <summary>Logs a failure with its exception and shows it as an error notice.</summary>
    private void Fail(string what, Exception error)
    {
        _host?.LogError(what, error);
        AddNotice(NoticeLevel.Error, $"{what}: {error.Message}");
    }

    /// <summary>Runs a user action; a failure is logged and becomes an error notice in the chat.</summary>
    private async Task RunAsync(string action, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OmpSupersededException)
        {
        }
        catch (Exception error)
        {
            if (!_disposed)
            {
                Fail($"{action} failed", error);
            }
        }
    }

    /// <summary><paramref name="work"/> once OMP is ready, joining a start already in progress.</summary>
    private Func<Task> WithService(Func<Task> work) => async () =>
    {
        await _host!.EnsureServiceAsync();
        await work();
    };

    private async Task SendAsync(Draft draft)
    {
        var service = _service!;
        ScrollToBottom();
        Task<PromptOutcome> outcome;
        try
        {
            await _host!.EnsureServiceAsync();
            var prompt = Attachments.Compose(draft.Text, draft.Attachments, service.Cwd);
            outcome = service.PromptAsync(prompt.Message, draft.Mode, prompt.Images.Count > 0 ? prompt.Images : null);
            await Task.WhenAny(outcome, Task.Delay(50));
            if (outcome.IsFaulted)
            {
                await outcome;
            }
        }
        catch (OmpSupersededException)
        {
            return;
        }
        catch (Exception error)
        {
            _composer!.Restore(draft);
            Fail("Prompt failed", error);

            return;
        }
        try
        {
            var result = await outcome;
            if (result.Status == PromptStatus.Error && !result.Admitted)
            {
                _composer!.Restore(draft);
                AddNotice(NoticeLevel.Error, $"Prompt failed: {result.Error ?? "OMP refused the prompt."}");
            }
        }
        catch (OmpSupersededException)
        {
        }
        catch (Exception error)
        {
            Fail("Prompt failed", error);
        }
    }

    private async Task SwitchSessionAsync(string path)
    {
        await _host!.EnsureServiceAsync();
        if (!string.Equals(path, _service!.Session.SessionFile, StringComparison.OrdinalIgnoreCase))
        {
            await _service.SwitchSessionAsync(path);
        }
    }

    /// <summary>Hides the conversation's scroll bar while the model picker covers the conversation, so it does not show beside the picker.</summary>
    private void SetTranscriptScrollBar(ScrollBarVisibility visibility)
    {
        if (Transcript.Template?.FindName("PART_Scroller", Transcript) is ScrollViewer scroller)
        {
            scroller.VerticalScrollBarVisibility = visibility;
        }
    }

    private async Task OpenModelPickerAsync()
    {
        var picker = _modelPicker;
        if (picker is null)
        {
            return;
        }

        picker.Open("Loading models…");
        await RunAsync("Select model", async () =>
        {
            try
            {
                await _host!.EnsureServiceAsync();
                var models = await _service!.ListModelsAsync();
                if (models.Count == 0)
                {
                    picker.SetStatus("OMP reports no available models. Configure providers in OMP.");

                    return;
                }
                var current = _session.Model;
                picker.SetModels(models, current is null ? null : new ModelKey(current.Provider, current.Id));
            }
            catch (Exception error) when (!(error is OmpSupersededException))
            {
                picker.SetStatus($"Listing models failed: {error.Message}");

                throw;
            }
        });
    }

    private void OpenEffortPicker()
    {
        var picker = _effortPicker;
        if (picker is null || _composer is null)
        {
            return;
        }

        var choices = Effort.Choices(_session);
        if (choices.Options.Count == 0)
        {
            AddNotice(NoticeLevel.Info, "The current model has no thinking levels.");

            return;
        }
        picker.Open();
        picker.SetSource(_ => choices.Options.Select(option =>
        {
            var current = option.Value == choices.Value;
            var label = Ui.Text((current ? "✓ " : "") + option.Label);
            var hint = option.Value == "auto" ? Ui.Subtle("OMP picks the effort for each turn") : null;

            return new PickerItem(option.Value, Ui.Column(0, label, hint), option.Label);
        }).ToList());
        if (choices.Value is not null)
        {
            picker.Select(value => (string)value == choices.Value);
        }
    }

    /// <summary>Shows or hides the session history over the sections and transcript; what it covers leaves the tab order.</summary>
    private void SetSessionHistoryVisible(bool visible)
    {
        HistoryHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SectionsArea.IsEnabled = !visible;
        TranscriptArea.IsEnabled = !visible;
    }

    private void CloseSessionHistory()
    {
        if (HistoryHost.Visibility != Visibility.Visible)
        {
            return;
        }

        SetSessionHistoryVisible(false);
        FocusInput();
    }

    private async Task OpenSessionPickerAsync()
    {
        var picker = _sessionPicker;
        if (picker is null)
        {
            return;
        }

        picker.Reset("Loading sessions…");
        SetSessionHistoryVisible(true);
        picker.FocusFirst();
        await RunAsync("Session history", async () =>
        {
            try
            {
                await _host!.EnsureServiceAsync();
                var sessions = await _service!.ListSessionsAsync();
                if (sessions.Count == 0)
                {
                    picker.SetStatus("No saved OMP sessions found.");

                    return;
                }
                var currentFile = _service.Session.SessionFile;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                picker.SetStatus(null);
                picker.SetSource(query => SessionList.Filter(sessions, query, Format.SessionTitle).Select(s => SessionItem(s, currentFile, now)).ToList());
            }
            catch (Exception error) when (!(error is OmpSupersededException))
            {
                picker.SetStatus($"Listing sessions failed: {error.Message}");

                throw;
            }
        });
    }

    private static PickerItem SessionItem(SessionSummary session, string? currentFile, long now)
    {
        var isCurrent = string.Equals(session.Path, currentFile, StringComparison.OrdinalIgnoreCase);
        var title = Format.SessionTitle(session);
        var description = $"{Format.RelativeTime(session.Modified, now)} · {Format.FormatBytes(session.Size)}{(isCurrent ? " · current" : "")}";
        var content = Ui.Column(0,
            Ui.Text((isCurrent ? "✓ " : "") + title),
            Ui.Subtle(description),
            string.IsNullOrEmpty(session.Cwd) ? null : Ui.Subtle(session.Cwd!));

        return new PickerItem(session, content, $"{title} {description}");
    }

    private void HookScroller()
    {
        var scroller = Transcript.Scroller;
        if (scroller is null || Equals(scroller.Tag, "hooked"))
        {
            return;
        }

        scroller.Tag = "hooked";
        scroller.ScrollChanged += (_, e) =>
        {
            if (_follow.OnScrollChanged(e.ExtentHeight, e.VerticalOffset, e.ViewportHeight, e.ExtentHeightChange, e.ViewportHeightChange, e.VerticalChange))
            {
                scroller.ScrollToBottom();
            }

            if (_jump is not null)
            {
                _jump.Visibility = _follow.JumpVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        scroller.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.PageUp || e.Key == Key.Up || e.Key == Key.Home)
            {
                _follow.Release();
            }

            if (e.Key == Key.PageDown || e.Key == Key.Down || e.Key == Key.End || e.Key == Key.Space)
            {
                ReaderScrolled();
            }
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            if ((Modifiers() & ModifierKeys.Control) == 0)
            {
                ReaderScrolled();
            }
        };
        scroller.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && InScrollBar(source))
            {
                _follow.ReaderScrolling = true;
            }
        };
        scroller.PreviewMouseLeftButtonUp += (_, __) =>
        {
            if (_follow.ReaderScrolling)
            {
                ReaderScrolled();
            }
        };
    }

    /// <summary>Marks the scroll changes of the current input as the reader's, until the input has been laid out.</summary>
    private void ReaderScrolled()
    {
        _follow.ReaderScrolling = true;
        _ = Dispatcher.InvokeAsync(() => _follow.ReaderScrolling = false, DispatcherPriority.ContextIdle);
    }

    private static bool InScrollBar(DependencyObject node)
    {
        for (var current = node; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private void ScrollToBottom()
    {
        _follow.ToBottom();
        if (_jump is not null)
        {
            _jump.Visibility = Visibility.Collapsed;
        }

        Transcript.Scroller?.ScrollToBottom();
    }

    /// <summary>Caps the composer and the cards at 40% of the visible height; both sit inside the zoom transform.</summary>
    private void ApplyHeights()
    {
        var cap = Math.Max(80, ActualHeight / ZoomTransform.ScaleY * 0.4);
        if (_composer is not null)
        {
            _composer.Input.MaxHeight = Math.Max(48, cap);
        }

        InteractionsScroller.MaxHeight = cap;
    }

    /// <summary>Opens a tool path as OMP reported it; the host resolves it against OMP's working directory and home.</summary>
    private void OpenFile(string path, int? line) => _ = RunAsync("Open file", () => _host!.OpenFileAsync(path, line));

    private void OpenDiff(string path, string? recordedBefore) => _ = RunAsync("Open diff", () => _host!.OpenDiffAsync(path, recordedBefore));

    private void OpenUrl(string url)
    {
        if (!MarkdownView.TryWebUri(url, out var uri))
        {
            AddNotice(NoticeLevel.Warning, $"Not opening a non-web URL: {url}");

            return;
        }
        try
        {
            LaunchUrl(uri!.AbsoluteUri);
        }
        catch (Exception error)
        {
            Fail($"Opening {url} failed", error);
        }
    }

    private void Copy(string text)
    {
        try
        {
            SetClipboard(text);
        }
        catch (Exception error)
        {
            Fail("Copy failed", error);
        }
    }

    /// <summary>Agent detail actions, routed to the service with errors surfaced in the chat.</summary>
    private sealed class AgentActions : IAgentActions
    {
        private readonly OmpChatControl _owner;

        public AgentActions(OmpChatControl owner) => _owner = owner;

        public RenderContext RenderContext => _owner._ctx!;

        public Task<IReadOnlyList<TranscriptItem>> TranscriptAsync(string agentId) => _owner._service!.GetAgentTranscriptAsync(agentId);

        public Task SteerAsync(string agentId, string message) => _owner._service!.SteerAgentAsync(agentId, message);

        public Task<bool> CancelAsync(string agentId) => _owner._service!.CancelAgentAsync(agentId);

        public Task RunAsync(string action, Func<Task> work) => _owner.RunAsync(action, work);

        public void Notice(NoticeLevel level, string text) => _owner.AddNotice(level, text);
    }
}
