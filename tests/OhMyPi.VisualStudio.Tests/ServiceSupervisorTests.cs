using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Omp.Core;
using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class ServiceSupervisorTests : IAsyncLifetime
{
    private sealed class Notifier : IServiceNotifier
    {
        private readonly object _gate = new();
        private readonly List<(string Message, string[] Actions)> _errors = new();
        private int _logShown;
        private int _settingsOpened;
        private int _closed;
        public Func<string, string?> Choose = _ => null;

        public IReadOnlyList<(string Message, string[] Actions)> Errors
        {
            get { lock (_gate) return _errors.ToArray(); }
        }

        public int LogShown => System.Threading.Volatile.Read(ref _logShown);
        public int SettingsOpened => System.Threading.Volatile.Read(ref _settingsOpened);
        public int Closed => System.Threading.Volatile.Read(ref _closed);

        public Task<string?> ShowErrorAsync(string message, params string[] actions)
        {
            lock (_gate) _errors.Add((message, actions));
            return Task.FromResult(Choose(message));
        }

        public void CloseErrors() => System.Threading.Interlocked.Increment(ref _closed);
        public void ShowLog() => System.Threading.Interlocked.Increment(ref _logShown);
        public void OpenSettings() => System.Threading.Interlocked.Increment(ref _settingsOpened);
    }

    private sealed class Logger : IOmpLogger
    {
        private readonly object _gate = new();
        private readonly List<(string Message, Exception? Error)> _errors = new();
        private readonly List<string> _infos = new();

        public IReadOnlyList<(string Message, Exception? Error)> Errors
        {
            get { lock (_gate) return _errors.ToArray(); }
        }

        public IReadOnlyList<string> Infos
        {
            get { lock (_gate) return _infos.ToArray(); }
        }

        public void Error(string message, Exception? error = null)
        {
            lock (_gate) _errors.Add((message, error));
        }

        public void Warn(string message, Exception? error = null) { }

        public void Info(string message, Exception? error = null)
        {
            lock (_gate) _infos.Add(message);
        }

        public void Debug(string message, Exception? error = null) { }
        public bool TraceEnabled => false;
        public void Trace(string direction, string frame) { }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<FakeOmpService> _services = new();
    private readonly List<IOmpService?> _consumed = new();
    private readonly List<(string Message, bool ExecutableNotFound)> _unavailable = new();
    private readonly List<(IOmpService Service, SessionView Session)> _sessions = new();
    private readonly Notifier _notifier = new();
    private readonly Logger _logger = new();
    private Exception? _createError;
    private bool _settingsChanged;
    private StartupMode _startup = StartupMode.ResumeLast;
    private readonly ServiceSupervisor _supervisor;

    public ServiceSupervisorTests()
    {
        _supervisor = new ServiceSupervisor(new ServiceSupervisorOptions
        {
            Create = () =>
            {
                if (_createError != null) throw _createError;
                var service = new FakeOmpService();
                lock (_services) _services.Add(service);
                return service;
            },
            SetService = service =>
            {
                lock (_consumed) _consumed.Add(service);
            },
            ShowUnavailable = (message, executableNotFound) =>
            {
                lock (_unavailable) _unavailable.Add((message, executableNotFound));
            },
            ResumeOptions = preferred => preferred == null ? null : new StartOptions { ResumeSessionFile = preferred },
            Startup = () => _startup,
            SettingsChanged = () => _settingsChanged,
            OnSession = (service, session) =>
            {
                lock (_sessions) _sessions.Add((service, session));
            },
            Logger = _logger,
            Notifier = _notifier,
        });
    }

    public ValueTask InitializeAsync() => default;

    public ValueTask DisposeAsync() => new(_supervisor.ShutdownAsync());

    /// <summary>Waits until background work made <paramref name="condition"/> true.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition did not become true in time.");
            await Task.Delay(1);
        }
    }

    private static async Task<T> Within<T>(Task<T> task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Timeout)));
        return await task;
    }

    private static async Task Within(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(Timeout)));
        await task;
    }

    /// <summary>True when <paramref name="task"/> is still pending after its continuations had time to run.</summary>
    private static async Task<bool> StillPending(Task task)
    {
        await Task.WhenAny(task, Task.Delay(100));
        return !task.IsCompleted;
    }

    private async Task<FakeOmpService> Ready()
    {
        _supervisor.Initialize();
        var service = _services[0];
        service.SetConnection(ConnectionState.Ready);
        service.Starts[0].Done.SetResult(true);
        await Within(_supervisor.EnsureServiceAsync());
        return service;
    }

    [Fact]
    public async Task CreatesTheServiceHandsItToTheConsumerAndStartsIt()
    {
        var service = await Ready();
        Assert.Equal(new IOmpService?[] { service }, _consumed);
        Assert.Single(service.Starts);
        Assert.Same(service, _supervisor.Service);
    }

    [Fact]
    public void StartsANewSessionInNewSessionMode()
    {
        _startup = StartupMode.NewSession;
        _supervisor.Initialize();
        Assert.True(_services[0].Starts[0].Options!.NewSession);
    }

    [Fact]
    public void DoesNotStartInManualMode()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        Assert.Empty(_services[0].Starts);
        Assert.True(_supervisor.NeedsStart());
    }

    [Fact]
    public async Task EnsureServiceStartsAManuallyStartedServiceWithTheGivenOptions()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        var ensured = _supervisor.EnsureServiceAsync(new StartOptions { NewSession = true });
        Assert.True(service.Starts[0].Options!.NewSession);
        service.Starts[0].Done.SetResult(true);
        Assert.Same(service, await Within(ensured));
    }

    [Fact]
    public async Task EnsureServiceCalledBeforeInitializeWaitsForTheService()
    {
        var ensured = _supervisor.EnsureServiceAsync();
        Assert.False(ensured.IsCompleted);
        _supervisor.Initialize();
        var service = _services[0];
        service.SetConnection(ConnectionState.Ready);
        service.Starts[0].Done.SetResult(true);
        Assert.Same(service, await Within(ensured));
    }

    [Fact]
    public void NeedsStartDoesNotHoldTheSupervisorLockWhileReadingTheConnection()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        var otherThreadGotTheService = true;
        service.OnConnectionRead = () =>
        {
            service.OnConnectionRead = null;
            otherThreadGotTheService = Task.Run(() => _supervisor.Service).Wait(TimeSpan.FromSeconds(2));
        };
        Assert.True(_supervisor.NeedsStart());
        Assert.True(otherThreadGotTheService, "a thread holding the service lock could not take the supervisor lock: lock-order inversion");
    }

    [Fact]
    public async Task ConcurrentEnsureCallsStartOmpOnce()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        Task<IOmpService>? second = null;
        service.BeforeStart = () => second = _supervisor.EnsureServiceAsync();
        var first = _supervisor.EnsureServiceAsync();
        Assert.Single(service.Starts);
        service.Starts[0].Done.SetResult(true);
        Assert.Same(service, await Within(first));
        Assert.Same(service, await Within(second!));
        Assert.Single(service.Starts);
    }

    [Fact]
    public async Task EnsureWaitsForARecreationInProgressInsteadOfFailing()
    {
        var service = await Ready();
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StopResult = stop.Task;
        var recreated = _supervisor.RecreateAsync();
        await Eventually(() => service.Stops == 1);
        var ensured = _supervisor.EnsureServiceAsync();
        Assert.True(await StillPending(ensured), "waits for the new service");
        stop.SetResult(true);
        await Within(recreated);
        var next = _services[1];
        next.SetConnection(ConnectionState.Ready);
        next.Starts[0].Done.SetResult(true);
        Assert.Same(next, await Within(ensured));
    }

    [Fact]
    public async Task EnsureWaitsWhileOmpRestartsOnItsOwn()
    {
        var service = await Ready();
        service.SetConnection(ConnectionState.Restarting);
        var ensured = _supervisor.EnsureServiceAsync();
        Assert.True(await StillPending(ensured), "waits for the automatic restart");
        service.SetConnection(ConnectionState.Ready);
        Assert.Same(service, await Within(ensured));
        Assert.Single(service.Starts);
    }

    [Fact]
    public async Task ReportsAFailedStartOnceWithShowLog()
    {
        _notifier.Choose = _ => "Show Log";
        _supervisor.Initialize();
        var service = _services[0];
        service.SetConnection(ConnectionState.Failed, "spawn ENOENT");
        service.Starts[0].Done.SetException(new InvalidOperationException("spawn ENOENT"));
        await Eventually(() => _notifier.LogShown == 1);
        Assert.Equal(new[] { "OMP failed to start: spawn ENOENT" }, _notifier.Errors.Select(e => e.Message));
        Assert.Equal(new[] { "Show Log", "Open Settings" }, _notifier.Errors[0].Actions);
    }

    [Fact]
    public async Task ReportsOmpStoppingOnItsOwnAndOffersARestart()
    {
        var service = await Ready();
        _notifier.Choose = _ => "Restart";
        service.SetConnection(ConnectionState.Failed, "OMP exited with code 1; gave up after 3 restarts");
        await Eventually(() => service.Starts.Count == 2);
        Assert.Equal("OMP stopped: OMP exited with code 1; gave up after 3 restarts", _notifier.Errors[0].Message);
        Assert.Equal(new[] { "Restart", "Show Log" }, _notifier.Errors[0].Actions);
    }

    [Fact]
    public async Task ClosesErrorNotificationsOnceOmpIsReady()
    {
        var service = await Ready();
        Assert.Equal(1, _notifier.Closed);
        service.SetConnection(ConnectionState.Failed, "crashed");
        service.SetConnection(ConnectionState.Ready);
        Assert.Equal(2, _notifier.Closed);
    }

    [Fact]
    public async Task DoesNotReportAStartThatARestartSuperseded()
    {
        _supervisor.Initialize();
        var service = _services[0];
        service.SetConnection(ConnectionState.Ready);
        var restarted = _supervisor.RestartAsync();
        service.Starts[0].Done.SetException(new OperationCanceledException("superseded"));
        await Eventually(() => service.Restarts.Count == 1);
        service.Restarts[0].SetResult(true);
        await Within(restarted);
        await Eventually(() => _logger.Infos.Contains("OMP start was superseded by a restart or stop"));
        Assert.Empty(_notifier.Errors);
        Assert.Single(service.Restarts);
    }

    [Fact]
    public async Task EnsureWaitsForTheRestartWhenItsStartIsSuperseded()
    {
        _supervisor.Initialize();
        var service = _services[0];
        service.SetConnection(ConnectionState.Ready);
        var ensured = _supervisor.EnsureServiceAsync();
        var restarted = _supervisor.RestartAsync();
        service.Starts[0].Done.SetException(new OperationCanceledException("superseded"));
        await Eventually(() => service.Restarts.Count == 1);
        Assert.True(await StillPending(ensured), "still waiting for the restart");
        service.Restarts[0].SetResult(true);
        Assert.Same(service, await Within(ensured));
        await Within(restarted);
    }

    [Fact]
    public async Task RunsOneRestartAtATime()
    {
        var service = await Ready();
        var first = _supervisor.RestartAsync();
        var second = _supervisor.RestartAsync();
        Assert.Same(first, second);
        await Eventually(() => service.Restarts.Count == 1);
        service.Restarts[0].SetResult(true);
        await Within(first);
        Assert.Single(service.Restarts);
    }

    [Fact]
    public async Task RestartStartsAStoppedService()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        var restarted = _supervisor.RestartAsync();
        await Eventually(() => service.Starts.Count == 1);
        Assert.Empty(service.Restarts);
        service.Starts[0].Done.SetResult(true);
        await Within(restarted);
    }

    [Fact]
    public async Task RestartRebuildsTheServiceWhenItsSettingsChanged()
    {
        var service = await Ready();
        service.SetSession(new SessionView { SessionFile = @"C:\sessions\a.jsonl" });
        _settingsChanged = true;
        var restarted = _supervisor.RestartAsync();
        await Eventually(() => _services.Count == 2);
        await Within(restarted);
        Assert.True(service.Disposed);
        Assert.Empty(service.Restarts);
        Assert.Equal(@"C:\sessions\a.jsonl", _services[1].Starts[0].Options!.ResumeSessionFile);
    }

    [Fact]
    public async Task DoesNotReportARestartFailureAsOmpStoppingOnItsOwn()
    {
        var service = await Ready();
        var restarted = _supervisor.RestartAsync();
        await Eventually(() => service.Restarts.Count == 1);
        service.SetConnection(ConnectionState.Failed, "spawn ENOENT");
        service.Restarts[0].SetException(new InvalidOperationException("spawn ENOENT"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Within(restarted));
        Assert.Equal("spawn ENOENT", error.Message);
        Assert.Empty(_notifier.Errors);
    }

    [Fact]
    public async Task StopDisposesTheServiceAndClearsTheConsumer()
    {
        var service = await Ready();
        await _supervisor.StopAsync();
        Assert.True(service.Disposed);
        Assert.Equal(1, service.Stops);
        Assert.Equal(new IOmpService?[] { service, null }, _consumed);
        Assert.Null(_supervisor.Service);
    }

    [Fact]
    public async Task ShutdownDuringAWorkingDirectoryChangeCreatesNoNewService()
    {
        var service = await Ready();
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StopResult = stop.Task;
        var reinitialized = _supervisor.ReinitializeAsync();
        await Eventually(() => service.Stops == 1);
        var shutdown = _supervisor.ShutdownAsync();
        stop.SetResult(true);
        await Within(reinitialized);
        await Within(shutdown);
        Assert.Single(_services);
        Assert.Null(_supervisor.Service);
        await _supervisor.RestartAsync();
        Assert.Single(_services);
    }

    [Fact]
    public async Task ReinitializeCreatesTheNewServiceEvenWhenTheOldOneFailsToStop()
    {
        var service = await Ready();
        service.StopResult = Task.FromException(new InvalidOperationException("did not exit after taskkill"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _supervisor.ReinitializeAsync());
        Assert.True(service.Disposed);
        Assert.Equal(2, _services.Count);
        Assert.Same(_services[1], _supervisor.Service);
    }

    [Fact]
    public async Task ReportsWhenTheRecreatedServiceCannotBeBuilt()
    {
        await Ready();
        _createError = new System.IO.FileNotFoundException("OMP executable not found");
        _notifier.Choose = _ => "Open Settings";
        await _supervisor.RecreateAsync();
        await Eventually(() => _notifier.SettingsOpened == 1);
        Assert.Equal(new[] { ("OMP executable not found", true) }, _unavailable);
        Assert.Equal("oh-my-pi: OMP executable not found", _notifier.Errors[0].Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _supervisor.EnsureServiceAsync());
    }

    [Fact]
    public async Task LogsTheStackOfAnUnexpectedServiceCreationFailure()
    {
        var bug = new NullReferenceException("bug");
        _createError = bug;
        _supervisor.Initialize();
        var logged = Assert.Single(_logger.Errors);
        Assert.Same(bug, logged.Error);
        Assert.Equal(new[] { ("bug", false) }, _unavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _supervisor.EnsureServiceAsync());
    }

    [Fact]
    public async Task RecreatesTheServiceAndResumesItsSession()
    {
        var service = await Ready();
        service.SetSession(new SessionView { SessionFile = @"C:\sessions\a.jsonl" });
        await _supervisor.RecreateAsync();
        Assert.Equal(1, service.Stops);
        Assert.Equal(2, _services.Count);
        Assert.Equal(@"C:\sessions\a.jsonl", _services[1].Starts[0].Options!.ResumeSessionFile);
    }

    [Fact]
    public async Task ReinitializeAppliesTheStartupModeToTheNewService()
    {
        var service = await Ready();
        service.SetSession(new SessionView { SessionFile = @"C:\sessions\a.jsonl" });
        _startup = StartupMode.Manual;
        await _supervisor.ReinitializeAsync();
        Assert.True(service.Disposed);
        Assert.Equal(2, _services.Count);
        Assert.Empty(_services[1].Starts);
        Assert.Same(_services[1], _supervisor.Service);
    }

    [Fact]
    public async Task ForwardsSessionChangesWithTheirService()
    {
        var service = await Ready();
        var session = new SessionView { SessionFile = "s.jsonl" };
        service.SetSession(session);
        var forwarded = Assert.Single(_sessions);
        Assert.Same(service, forwarded.Service);
        Assert.Same(session, forwarded.Session);
    }

    [Fact]
    public async Task IgnoresEventsOfAReplacedService()
    {
        var old = await Ready();
        await _supervisor.RecreateAsync();
        old.SetSession(new SessionView { SessionFile = "stale.jsonl" });
        old.SetConnection(ConnectionState.Failed, "stale");
        Assert.Empty(_sessions);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(_notifier.Errors);
    }

    [Fact]
    public async Task RestartWithoutServiceCreatesAndStartsOne()
    {
        _createError = new System.IO.FileNotFoundException("not found");
        _supervisor.Initialize();
        Assert.Empty(_services);
        _createError = null;
        await _supervisor.RestartAsync();
        Assert.Single(_services[0].Starts);
    }

    [Fact]
    public async Task NewSessionStartsAStoppedOmpInANewSession()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        var created = _supervisor.NewSessionAsync();
        Assert.True(service.Starts[0].Options!.NewSession);
        service.Starts[0].Done.SetResult(true);
        Assert.Same(service, await Within(created));
        Assert.Equal(0, service.NewSessions);
    }

    [Fact]
    public async Task NewSessionAsksARunningOmpForANewSession()
    {
        var service = await Ready();
        Assert.Same(service, await Within(_supervisor.NewSessionAsync()));
        Assert.Equal(1, service.NewSessions);
    }

    [Fact]
    public async Task NewSessionRacingAnotherStartStillOpensANewSession()
    {
        _startup = StartupMode.Manual;
        _supervisor.Initialize();
        var service = _services[0];
        Task<IOmpService>? resume = null;
        var reads = 0;
        service.OnConnectionRead = () =>
        {
            if (++reads != 2) return;
            service.OnConnectionRead = null;
            resume = _supervisor.EnsureServiceAsync(new StartOptions { ResumeSessionFile = "a.jsonl" });
        };
        var created = _supervisor.NewSessionAsync();
        var start = Assert.Single(service.Starts);
        start.Done.SetResult(true);
        Assert.Same(service, await Within(created));
        if (resume != null) await Within(resume);
        var freshSessions = (start.Options?.NewSession == true ? 1 : 0) + service.NewSessions;
        Assert.Equal(1, freshSessions);
    }

    [Fact]
    public async Task OfferingShowLogAfterOmpStoppedOpensTheLog()
    {
        var service = await Ready();
        _notifier.Choose = _ => "Show Log";
        service.SetConnection(ConnectionState.Failed, "crashed");
        await Eventually(() => _notifier.LogShown == 1);
        Assert.Single(service.Starts);
    }

    [Fact]
    public async Task ARestartChosenAfterOmpStoppedThatFailsIsReportedAsAFailedStart()
    {
        var service = await Ready();
        _notifier.Choose = message => message.StartsWith("OMP stopped", StringComparison.Ordinal) ? "Restart" : null;
        service.SetConnection(ConnectionState.Failed, "crashed");
        await Eventually(() => service.Starts.Count == 2);
        service.Starts[1].Done.SetException(new InvalidOperationException("still broken"));
        await Eventually(() => _notifier.Errors.Any(e => e.Message == "OMP failed to start: still broken"));
    }

    [Fact]
    public async Task ANotificationThatFailsIsLoggedAndDoesNotBreakTheSupervisor()
    {
        var service = await Ready();
        _notifier.Choose = _ => throw new InvalidOperationException("info bar gone");
        service.SetConnection(ConnectionState.Failed, "crashed");
        await Eventually(() => _logger.Errors.Any(e => e.Message == "oh-my-pi notification failed"));
        Assert.NotNull(_supervisor.Service);
    }

    [Fact]
    public async Task ARestartThatAStopSupersedesIsOnlyLogged()
    {
        var service = await Ready();
        var restart = _supervisor.RestartAsync();
        await Eventually(() => service.Restarts.Count == 1);
        service.Restarts[0].SetException(new OperationCanceledException());
        await Within(restart);
        Assert.Contains("OMP restart was superseded by a stop", _logger.Infos);
    }
}
