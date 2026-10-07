using Omp.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>Non-modal error notifications with actions; the task completes with the chosen action or null.</summary>
internal interface IServiceNotifier
{
    Task<string?> ShowErrorAsync(string message, params string[] actions);

    /// <summary>Closes every error notification still on screen; their tasks complete with null. Never blocks.</summary>
    void CloseErrors();

    void ShowLog();

    void OpenSettings();
}

internal sealed class ServiceSupervisorOptions
{
    /// <summary>Builds a service from the current settings; throws <see cref="FileNotFoundException"/> when OMP is not installed.</summary>
    public Func<IOmpService> Create { get; set; } = null!;

    /// <summary>Hands the current service (null while there is none) to the surfaces.</summary>
    public Action<IOmpService?> SetService { get; set; } = null!;

    /// <summary>Shows on every surface why there is no service: the message, and whether the OMP executable is missing (otherwise building the service failed).</summary>
    public Action<string, bool> ShowUnavailable { get; set; } = null!;

    /// <summary>Session to bind when OMP starts without an explicit choice; the argument wins while its file exists.</summary>
    public Func<string?, StartOptions?> ResumeOptions { get; set; } = null!;

    public Func<StartupMode> Startup { get; set; } = null!;

    /// <summary>True when the settings that shape the OMP process differ from those the current service was built with.</summary>
    public Func<bool> SettingsChanged { get; set; } = () => false;

    /// <summary>Raised on a background thread with the service that reported the session.</summary>
    public Action<IOmpService, SessionView> OnSession { get; set; } = null!;

    public IOmpLogger Logger { get; set; } = null!;

    public IServiceNotifier Notifier { get; set; } = null!;
}

/// <summary>
/// Owns the OMP service: creation from settings, start, restart, recreation and teardown.
/// Restarts and recreations run one at a time.
/// </summary>
/// <remarks>
/// Never call into a service while holding <see cref="_gate"/>: a service raises its events under its own lock,
/// and the handlers here take <see cref="_gate"/>, so the opposite order deadlocks.
/// </remarks>
internal sealed class ServiceSupervisor
{
    private const string NotAvailableMessage = "OMP is not available";

    private readonly ServiceSupervisorOptions _options;
    private readonly object _gate = new object();
    private readonly TaskCompletionSource<bool> _initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private IOmpService? _current;
    private string? _unavailable;
    /// <summary>Start or restart of the current service requested here and not settled yet.</summary>
    private Task? _starting;
    private object? _startingToken;
    /// <summary>Restart or recreation in progress; the next one waits for it.</summary>
    private Task? _changing;
    private object? _changingToken;
    private bool _shutDown;

    public ServiceSupervisor(ServiceSupervisorOptions options)
    {
        _options = options;
    }

    public IOmpService? Service
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Completes once <see cref="Initialize"/> ran, so a service or the reason there is none exists.</summary>
    public Task WhenInitializedAsync() =>
#pragma warning disable VSTHRD003 // Completed by Initialize on any thread; it never needs the caller's thread.
        _initialized.Task;

#pragma warning restore VSTHRD003

    /// <summary>Creates the service and starts it according to the Startup option.</summary>
    public void Initialize()
    {
        try
        {
            if (!CreateService())
            {
                return;
            }

            var startup = _options.Startup();
            if (startup == StartupMode.Manual)
            {
                _options.Logger.Info("Startup mode manual: OMP starts on the first command");

                return;
            }
            StartInBackground(startup == StartupMode.NewSession ? new StartOptions { NewSession = true } : _options.ResumeOptions(null));
        }
        finally
        {
            _initialized.TrySetResult(true);
        }
    }

    /// <summary>True when OMP has not been started (manual startup or after a failure).</summary>
    public bool NeedsStart()
    {
        IOmpService? service;
        lock (_gate)
        {
            if (_starting is not null)
            {
                return false;
            }

            service = _current;
        }

        return service is not null && IsStopped(service.Connection.State);
    }

    /// <summary>
    /// The running service; starts OMP with <paramref name="options"/> when it is not running yet, and waits for a
    /// recreation, a start or an automatic restart in progress.
    /// </summary>
    public async Task<IOmpService> EnsureServiceAsync(StartOptions? options = null) =>
        (await EnsureCoreAsync(options).ConfigureAwait(false)).Service;

    /// <summary>The running service in a fresh session: a start made here opens one, a running OMP is asked for one.</summary>
    public async Task<IOmpService> NewSessionAsync()
    {
        var (service, startedHere) = await EnsureCoreAsync(new StartOptions { NewSession = true }).ConfigureAwait(false);
        if (!startedHere)
        {
            await service.NewSessionAsync().ConfigureAwait(false);
        }

        return service;
    }

    /// <returns>The service, and whether it was started here with <paramref name="options"/>.</returns>
    private async Task<(IOmpService Service, bool StartedHere)> EnsureCoreAsync(StartOptions? options)
    {
        await WhenInitializedAsync().ConfigureAwait(false);
        while (true)
        {
            IOmpService? service;
            Task? changing;
            lock (_gate)
            {
                service = _current;
                changing = _shutDown ? null : _changing;
                if (service is null && changing is null)
                {
                    throw new InvalidOperationException(_unavailable ?? NotAvailableMessage);
                }
            }
            if (service is null)
            {
                await SettledAsync(changing!).ConfigureAwait(false);
                continue;
            }

            var state = service.Connection.State;
            Task? launch;
            TaskCompletionSource<bool>? reserved = null;
            object? token = null;
            lock (_gate)
            {
                if (_current != service)
                {
                    continue;
                }

                launch = _starting;
                if (launch is null && IsStopped(state))
                {
                    reserved = ReserveStartLocked(out token);
                    launch = reserved.Task;
                }
            }
            if (reserved is not null)
            {
                BeginStart(() => service.StartAsync(options), token!, reserved);
            }

            if (launch is null)
            {
                if (!IsTransitional(state))
                {
                    return (service, false);
                }

                await SettledStateAsync(service).ConfigureAwait(false);
                continue;
            }

            try
            {
#pragma warning disable VSTHRD003 // A start tracked by this supervisor; it runs on the thread pool.
                await launch.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                return (service, reserved is not null);
            }
            catch (Exception error) when (IsSuperseded(error))
            {
                lock (_gate)
                {
                    if (_starting is not null || _current != service)
                    {
                        continue;
                    }

                    changing = _changing;
                }
                if (changing is null)
                {
                    throw;
                }

                await SettledAsync(changing).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Restarts OMP, or starts it when it is not running; a restart requested while one runs joins it.</summary>
    public Task RestartAsync()
    {
        lock (_gate)
        {
#pragma warning disable VSTHRD003 // Joins the change this supervisor runs on the thread pool.
            if (_changing is not null)
            {
                return _changing;
            }
#pragma warning restore VSTHRD003
        }

        return ChangeAsync(RestartNowAsync);
    }

    /// <summary>Rebuilds the service with fresh settings, keeping the current session.</summary>
    public Task RecreateAsync() => ChangeAsync(RebuildAsync);

    /// <summary>Rebuilds the service for a new working directory and applies the Startup option again, even when the old service failed to stop.</summary>
    public Task ReinitializeAsync() => ChangeAsync(async () =>
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            Initialize();
        }
    });

    public async Task StopAsync()
    {
        IOmpService? service;
        lock (_gate)
        {
            service = _current;
            _current = null;
            _starting = null;
            _startingToken = null;
        }
        if (service is null)
        {
            return;
        }

        Unsubscribe(service);
        _options.SetService(null);
        try
        {
            await service.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            service.Dispose();
        }
    }

    /// <summary>
    /// Stops the service for good: later recreations, restarts and starts do nothing. Stopping first supersedes a
    /// start the change in progress waits for; the change is then joined.
    /// </summary>
    public async Task ShutdownAsync()
    {
        Task? changing;
        lock (_gate)
        {
            _shutDown = true;
            changing = _changing;
        }
        _initialized.TrySetResult(true);
        await StopAsync().ConfigureAwait(false);
        if (changing is not null)
        {
            await SettledAsync(changing).ConfigureAwait(false);
        }
    }

    private async Task RebuildAsync()
    {
        var sessionFile = Service?.Session.SessionFile;
        await StopAsync().ConfigureAwait(false);
        if (!CreateService())
        {
            return;
        }

        StartInBackground(_options.ResumeOptions(sessionFile));
    }

    private bool CreateService()
    {
        lock (_gate)
        {
            if (_shutDown)
            {
                return false;
            }
        }
        IOmpService service;
        try
        {
            service = _options.Create();
        }
        catch (FileNotFoundException error)
        {
            MarkUnavailable(error.Message, null);

            return false;
        }
        catch (Exception error)
        {
            MarkUnavailable(error.Message, error);

            return false;
        }
        bool shutDown;
        lock (_gate)
        {
            shutDown = _shutDown;
            if (!shutDown)
            {
                _current = service;
                _unavailable = null;
            }
        }
        if (shutDown)
        {
            service.Dispose();

            return false;
        }
        service.SessionChanged += OnSessionChanged;
        service.ConnectionChanged += OnConnectionChanged;
        _options.SetService(service);

        return true;
    }

    private void Unsubscribe(IOmpService service)
    {
        service.SessionChanged -= OnSessionChanged;
        service.ConnectionChanged -= OnConnectionChanged;
    }

    /// <param name="error">The unexpected failure, logged with its stack; null when the OMP executable is missing.</param>
    private void MarkUnavailable(string message, Exception? error)
    {
        lock (_gate)
        {
            _unavailable = message;
        }

        _options.Logger.Error($"OMP not available: {message}", error);
        _options.ShowUnavailable(message, error is null);
        Fire(async () =>
        {
            if (await _options.Notifier.ShowErrorAsync($"oh-my-pi: {message}", "Open Settings").ConfigureAwait(false) == "Open Settings")
            {
                _options.Notifier.OpenSettings();
            }
        });
    }

    private void OnSessionChanged(object sender, SessionView session)
    {
        if (!(sender is IOmpService service) || service != Service)
        {
            return;
        }

        _options.OnSession(service, session);
    }

    /// <summary>
    /// Ready closes the failure notifications that no longer apply. A start or restart requested here reports its own
    /// failure; any other failure means OMP stopped on its own.
    /// </summary>
    private void OnConnectionChanged(object sender, ConnectionStatus status)
    {
        if (status.State == ConnectionState.Ready)
        {
            if (sender == Service)
            {
                _options.Notifier.CloseErrors();
            }

            return;
        }
        if (status.State != ConnectionState.Failed)
        {
            return;
        }

        lock (_gate)
        {
            if (_starting is not null || sender != _current)
            {
                return;
            }
        }
        Fire(async () =>
        {
            var choice = await _options.Notifier.ShowErrorAsync($"OMP stopped: {status.Detail ?? "unknown reason"}", "Restart", "Show Log").ConfigureAwait(false);
            if (choice == "Restart")
            {
                try
                {
                    await RestartAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    await ReportStartFailureAsync(error).ConfigureAwait(false);
                }
            }
            else if (choice == "Show Log")
            {
                _options.Notifier.ShowLog();
            }
        });
    }

    private async Task RestartNowAsync()
    {
        var service = Service;
        if (service is null)
        {
            if (CreateService())
            {
                StartInBackground(_options.ResumeOptions(null));
            }

            return;
        }
        if (_options.SettingsChanged())
        {
            await RebuildAsync().ConfigureAwait(false);

            return;
        }
        if (NeedsStart())
        {
            await StartAsync(_options.ResumeOptions(null)).ConfigureAwait(false);

            return;
        }
        try
        {
            await TrackAsync(service.RestartAsync).ConfigureAwait(false);
        }
        catch (Exception error) when (IsSuperseded(error))
        {
            _options.Logger.Info("OMP restart was superseded by a stop");
        }
    }

    private void StartInBackground(StartOptions? options) => Fire(async () =>
                                                                  {
                                                                      try
                                                                      {
                                                                          await StartAsync(options).ConfigureAwait(false);
                                                                      }
                                                                      catch (Exception error)
                                                                      {
                                                                          await ReportStartFailureAsync(error).ConfigureAwait(false);
                                                                      }
                                                                  });

    private async Task ReportStartFailureAsync(Exception error)
    {
        if (IsSuperseded(error))
        {
            _options.Logger.Info("OMP start was superseded by a restart or stop");

            return;
        }
        _options.Logger.Error("OMP failed to start", error);
        var choice = await _options.Notifier.ShowErrorAsync($"OMP failed to start: {error.Message}", "Show Log", "Open Settings").ConfigureAwait(false);
        if (choice == "Show Log")
        {
            _options.Notifier.ShowLog();
        }
        else if (choice == "Open Settings")
        {
            _options.Notifier.OpenSettings();
        }
    }

    private Task StartAsync(StartOptions? options)
    {
        var service = Service;
        if (service is null)
        {
            string? unavailable;
            lock (_gate)
            {
                unavailable = _unavailable;
            }

            return Task.FromException(new InvalidOperationException(unavailable ?? NotAvailableMessage));
        }

        return TrackAsync(() => service.StartAsync(options));
    }

    /// <summary>Runs <paramref name="launch"/> as the start in progress, superseding any earlier one.</summary>
    private Task TrackAsync(Func<Task> launch)
    {
        TaskCompletionSource<bool> settled;
        object token;
        lock (_gate)
        {
            settled = ReserveStartLocked(out token);
        }

        BeginStart(launch, token, settled);

        return settled.Task;
    }

    private TaskCompletionSource<bool> ReserveStartLocked(out object token)
    {
        token = new object();
        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _startingToken = token;
        _starting = settled.Task;

        return settled;
    }

    /// <summary>Calls <paramref name="launch"/> outside the lock; the reservation is released before its waiters resume.</summary>
    private void BeginStart(Func<Task> launch, object token, TaskCompletionSource<bool> settled) => _ = SettleStartAsync(launch, token, settled);

    private async Task SettleStartAsync(Func<Task> launch, object token, TaskCompletionSource<bool> settled)
    {
        Exception? failure = null;
        try
        {
            await launch().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
        }
        lock (_gate)
        {
            if (_startingToken == token)
            {
                _starting = null;
                _startingToken = null;
            }
        }
        if (failure is null)
        {
            settled.TrySetResult(true);
        }
        else
        {
            settled.TrySetException(failure);
        }
    }

    /// <summary>Waits until <paramref name="service"/> leaves the starting and restarting states.</summary>
    private static async Task SettledStateAsync(IOmpService service)
    {
        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object sender, ConnectionStatus status)
        {
            if (!IsTransitional(status.State))
            {
                settled.TrySetResult(true);
            }
        }
        service.ConnectionChanged += OnChanged;
        try
        {
            if (!IsTransitional(service.Connection.State))
            {
                return;
            }
#pragma warning disable VSTHRD003 // Completed by the service's own connection events.
            await settled.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            service.ConnectionChanged -= OnChanged;
        }
    }

    private Task ChangeAsync(Func<Task> run)
    {
        var token = new object();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task tracked;
        lock (_gate)
        {
            var previous = _changing;
            _changingToken = token;
            tracked = ChangeCoreAsync(previous, gate.Task, run, token);
            if (_changingToken == token)
            {
                _changing = tracked;
            }
        }
        gate.SetResult(true);

        return tracked;
    }

    private async Task ChangeCoreAsync(Task? previous, Task gate, Func<Task> run, object token)
    {
        try
        {
#pragma warning disable VSTHRD003 // The gate is completed by ChangeAsync right after this change is registered; the previous change runs on the thread pool.
            await gate.ConfigureAwait(false);
            if (previous is not null)
            {
                await SettledAsync(previous).ConfigureAwait(false);
            }
#pragma warning restore VSTHRD003
            await run().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_changingToken == token)
                {
                    _changing = null;
                    _changingToken = null;
                }
            }
        }
    }

    /// <summary>Waits for a task whose failure its own caller reports.</summary>
    private static Task SettledAsync(Task task) =>
        task.ContinueWith(_ => { }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static bool IsStopped(ConnectionState state) => state == ConnectionState.Stopped || state == ConnectionState.Failed;

    private static bool IsTransitional(ConnectionState state) => state == ConnectionState.Starting || state == ConnectionState.Restarting;

    private static bool IsSuperseded(Exception error) => error is OperationCanceledException;

    private void Fire(Func<Task> work) => _ = FireAsync(work);

    private async Task FireAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _options.Logger.Error("oh-my-pi notification failed", error);
        }
    }
}
