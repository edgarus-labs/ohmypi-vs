using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.Logic.Automation;
using OhMyPi.VisualStudio.UI;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio;

/// <summary>One OMP service with the host services its chat control generation uses.</summary>
internal sealed class Generation
{
    public Generation(IOmpService service, VsOmpHost host, bool ownsService)
    {
        Service = service;
        Host = host;
        OwnsService = ownsService;
    }

    public IOmpService Service { get; }

    public VsOmpHost Host { get; }

    /// <summary>True for the never-started placeholder that carries the banner saying why OMP is unavailable.</summary>
    public bool OwnsService { get; }
}

/// <summary>
/// Hosts the single OMP service of this VS instance: working directory from the open solution or
/// folder, executable and options from Tools &gt; Options, the last session per directory, and a new
/// service (and chat control generation) whenever the working directory changes.
/// </summary>
internal sealed class OmpRuntime : IDisposable
{
    private const string LastSessionCollection = "OhMyPi\\LastSessions";
    private const string ModelPreferencesCollection = "OhMyPi\\ModelPicker";
    private const string DismissalsCollection = "OhMyPi\\Dismissed";
    private static readonly TimeSpan SolutionChangeDelay = TimeSpan.FromMilliseconds(500);
    /// <summary>How long closing Visual Studio waits for OMP to exit gracefully before the job object ends it.</summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);

    private readonly WritableSettingsStore _store;
    private readonly object _gate = new object();
    private readonly SessionMemory _sessions = new SessionMemory();
    private string _cwd = "";
    private string? _solutionDirectory;
    private Generation? _generation;
    private ProcessSettings _applied;
    private SessionPhase? _phase;
    private CancellationTokenSource? _solutionChange;
    private bool _disposed;

    private OmpRuntime(OmpPackage package, OmpOptionsPage options, OutputWindowLogger logger, WritableSettingsStore store)
    {
        Package = package;
        Options = options;
        Logger = logger;
        _store = store;
        ModelPreferences = new ModelPreferenceStore(new SettingsPreferenceStore(store, ModelPreferencesCollection));
        Dismissals = new DismissalStore(new SettingsPreferenceStore(store, DismissalsCollection));
        _applied = ProcessSettings.From(options);
        ActiveDocument = new ActiveDocumentMonitor(package);
        Notifier = new InfoBarNotifier(package, logger, logger.Show, OpenSettings);
        Supervisor = new ServiceSupervisor(new ServiceSupervisorOptions
        {
            Create = CreateService,
            SetService = OnServiceChanged,
            ShowUnavailable = OnUnavailable,
            ResumeOptions = preferred => LastSession.ResumeOptions(preferred, _sessions.Last, File.Exists, message => Logger.Info(message)),
            Startup = () => Options.Startup,
            SettingsChanged = SettingsChanged,
            OnSession = OnSessionChanged,
            Logger = logger,
            Notifier = Notifier,
        });
    }

    public OmpPackage Package { get; }

    public OmpOptionsPage Options { get; }

    public OutputWindowLogger Logger { get; }

    public InfoBarNotifier Notifier { get; }

    public ActiveDocumentMonitor ActiveDocument { get; }

    public ServiceSupervisor Supervisor { get; }

    public IModelPreferences ModelPreferences { get; }

    public DismissalStore Dismissals { get; }

    /// <summary>Raised on any thread after <see cref="Current"/> was replaced.</summary>
    public event EventHandler? GenerationChanged;

    public Generation? Current
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public static async Task<OmpRuntime> CreateAsync(OmpPackage package, OmpOptionsPage options)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var logger = await OutputWindowLogger.CreateAsync(package, options);
        var store = new ShellSettingsManager(package).GetWritableSettingsStore(SettingsScope.UserSettings);

        return new OmpRuntime(package, options, logger, store);
    }

    /// <summary>Resolves the working directory and follows solution, folder and option changes. UI thread.</summary>
    public void Initialize()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        UpdateWorkingDirectory();
        SolutionEvents.OnAfterOpenSolution += OnSolutionChanged;
        SolutionEvents.OnAfterCloseSolution += OnSolutionChanged;
        SolutionEvents.OnAfterOpenFolder += OnFolderChanged;
        SolutionEvents.OnAfterCloseFolder += OnFolderChanged;
        Options.Applied += OnOptionsApplied;
    }

    /// <summary>Creates the OMP service and starts it according to the Startup option, then removes diff copies a crashed Visual Studio left behind. Off the UI thread.</summary>
    public async Task StartAsync()
    {
        await TaskScheduler.Default;
        Supervisor.Initialize();
        try
        {
            foreach (var (root, error) in DiffBaselines.SweepStale(ChangeTracker.DiffDirectory, IsRunning))
            {
                Logger.Warn($"Could not delete the stale OMP diff copies in {root}", error);
            }
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Logger.Warn($"Could not look for stale OMP diff copies in {ChangeTracker.DiffDirectory}", error);
        }
    }

    public void OpenSettings() => Background.Run(Package.JoinableTaskFactory, Logger, "Opening oh-my-pi settings", async () =>
                                       {
                                           await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
                                           Package.ShowOptionPage(typeof(OmpOptionsPage));
                                       });

    /// <summary>Opens a path named by OMP (relative to the working directory of <paramref name="scope"/>) as a preview tab, optionally at a 1-based line.</summary>
    public async Task OpenFileAsync(WorkspaceScope scope, string rawPath, int? line)
    {
        var path = scope.Resolve(rawPath);
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        if (!await scope.MayOpenAsync(path, ConfirmOutsideWorkspaceAsync))
        {
            return;
        }

        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        using (new NewDocumentStateScope(__VSNEWDOCUMENTSTATE.NDS_Provisional, VSConstants.NewDocumentStateReason.Navigation))
        {
            VsShellUtilities.OpenDocument(Package, path, Guid.Empty, out _, out _, out _, out var view);
            if (line > 0 && view is not null)
            {
                view.SetCaretPos(line.Value - 1, 0);
                view.CenterLines(line.Value - 1, 1);
            }
        }
    }

    /// <summary>Asks whether a path from OMP's tool output that lies outside the workspace may be opened.</summary>
    public async Task<bool> ConfirmOutsideWorkspaceAsync(string path)
    {
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        var result = VsShellUtilities.ShowMessageBox(
            Package,
            $"{path} is outside the workspace. Open it anyway?\n\nThe path comes from OMP's tool output.",
            "oh-my-pi",
            OLEMSGICON.OLEMSGICON_WARNING,
            OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

        return result == (int)VSConstants.MessageBoxResult.IDOK;
    }

    /// <summary>Shows the chat when OMP asks something while it is not on screen; the request is never answered here.</summary>
    public async Task RevealForInteractionAsync()
    {
        await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
        if (await Package.IsChatOnScreenAsync())
        {
            return;
        }

        await Package.ShowChatAsync(activate: false);
    }

    /// <summary>Called by the package on the UI thread; waits a bounded time for OMP to exit.</summary>
    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Generation? generation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            generation = _generation;
            _generation = null;
        }
        SolutionEvents.OnAfterOpenSolution -= OnSolutionChanged;
        SolutionEvents.OnAfterCloseSolution -= OnSolutionChanged;
        SolutionEvents.OnAfterOpenFolder -= OnFolderChanged;
        SolutionEvents.OnAfterCloseFolder -= OnFolderChanged;
        Options.Applied -= OnOptionsApplied;
        ActiveDocument.Dispose();
        _solutionChange?.Cancel();
        if (generation is not null)
        {
            Release(generation);
        }

        var shutdown = Task.Run(Supervisor.ShutdownAsync);
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
#pragma warning disable VSTHRD003 // The shutdown runs on the thread pool and never needs the UI thread this blocks.
            if (await Task.WhenAny(shutdown, Task.Delay(ShutdownWait)) != shutdown)
            {
                Logger.Warn($"OMP did not stop within {ShutdownWait.TotalSeconds:0} s; it ends with Visual Studio");

                return;
            }
            try
            {
                await shutdown;
            }
            catch (Exception error)
            {
                Logger.Error("Stopping OMP failed", error);
            }
#pragma warning restore VSTHRD003
        });
    }

    private IOmpService CreateService()
    {
        var executable = ExecutableResolver.Locate(Options.ExecutablePath);
        var settings = ProcessSettings.From(Options);
        string cwd;
        lock (_gate)
        {
            _applied = settings;
            cwd = _cwd;
        }
        Logger.Info($"Using OMP executable {executable} (cwd {cwd})");

        return new OmpService(new OmpServiceOptions
        {
            Executable = executable,
            ExtraArgs = CommandLine.Split(settings.ExtraArgs),
            Cwd = cwd,
            Logger = Logger,
            HostTools = new VsHostTools(new VsAutomation(Package), ScopeFor(cwd)),
            AutoRestart = settings.AutoRestart,
        });
    }

    private bool SettingsChanged()
    {
        var current = ProcessSettings.From(Options);
        lock (_gate)
        {
            return !current.Equals(_applied);
        }
    }

    /// <summary>Where the tool paths of a service in <paramref name="cwd"/> are trusted: that directory and the solution directory.</summary>
    private WorkspaceScope ScopeFor(string cwd)
    {
        lock (_gate)
        {
            var roots = new List<string> { cwd };
            if (_solutionDirectory is not null && !WorkingDirectory.Same(_solutionDirectory, cwd))
            {
                roots.Add(_solutionDirectory);
            }

            return new WorkspaceScope(cwd, roots);
        }
    }

    private void OnServiceChanged(IOmpService? service)
    {
        if (service is null)
        {
            return;
        }

        Swap(new Generation(service, new VsOmpHost(this, service, ScopeFor(service.Cwd), null), ownsService: false));
    }

    private void OnUnavailable(string message, bool executableNotFound)
    {
        string cwd;
        lock (_gate)
        {
            cwd = _cwd;
        }

        var placeholder = new OmpService(new OmpServiceOptions { Executable = "omp", Cwd = cwd, Logger = Logger, AutoRestart = false });
        Swap(new Generation(placeholder, new VsOmpHost(this, placeholder, ScopeFor(cwd), new OmpUnavailable(message, executableNotFound)), ownsService: true));
    }

    private void Swap(Generation next)
    {
        Generation? previous;
        bool disposed;
        lock (_gate)
        {
            disposed = _disposed;
            previous = disposed ? next : _generation;
            if (!disposed)
            {
                _generation = next;
            }
        }
        if (!disposed)
        {
            next.Service.InteractionRequested += OnInteractionRequested;
        }

        if (previous is not null)
        {
            Release(previous);
        }

        if (disposed)
        {
            return;
        }

        GenerationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Release(Generation generation)
    {
        generation.Service.InteractionRequested -= OnInteractionRequested;
        generation.Host.Dispose();
        if (generation.OwnsService)
        {
            generation.Service.Dispose();
        }
    }

    private void OnInteractionRequested(object sender, InteractionRequest request) => Background.Run(Package.JoinableTaskFactory, Logger, "Revealing oh-my-pi for an OMP request", RevealForInteractionAsync);

    private void OnSessionChanged(IOmpService service, SessionView session)
    {
        RefreshCommandsOnPhaseChange(session.Phase);
        var file = session.SessionFile;
        if (!_sessions.Record(service.Cwd, file))
        {
            return;
        }

        var key = LastSession.KeyFor(service.Cwd);
        Background.Run(Package.JoinableTaskFactory, Logger, "Persisting the last OMP session file", async () =>
        {
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
            if (!_store.CollectionExists(LastSessionCollection))
            {
                _store.CreateCollection(LastSessionCollection);
            }

            _store.SetString(LastSessionCollection, key, file);
        });
    }

    /// <summary>Abort's enabled state comes from its query-status handler, which VS calls again only when asked to.</summary>
    private void RefreshCommandsOnPhaseChange(SessionPhase phase)
    {
        lock (_gate)
        {
            if (_phase == phase)
            {
                return;
            }

            _phase = phase;
        }
        Background.Run(Package.JoinableTaskFactory, Logger, "Refreshing the OMP commands", async () =>
        {
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
            var shell = await Package.GetServiceAsync<SVsUIShell, IVsUIShell>();
            ErrorHandler.ThrowOnFailure(shell.UpdateCommandUI(0));
        });
    }

    private void OnSolutionChanged(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ScheduleWorkingDirectoryCheck();
    }

    private void OnFolderChanged(object sender, FolderEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ScheduleWorkingDirectoryCheck();
    }

    /// <summary>Waits briefly so closing one solution and opening another restarts OMP once.</summary>
    private void ScheduleWorkingDirectoryCheck()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _solutionChange?.Cancel();
        var change = new CancellationTokenSource();
        _solutionChange = change;
        Background.Run(Package.JoinableTaskFactory, Logger, "Following the solution change", async () =>
        {
            try
            {
                await Task.Delay(SolutionChangeDelay, change.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await Package.JoinableTaskFactory.SwitchToMainThreadAsync(Package.DisposalToken);
            if (change.IsCancellationRequested || !UpdateWorkingDirectory())
            {
                return;
            }

            Logger.Info("Solution or folder changed; restarting OMP in the new working directory");
            await TaskScheduler.Default;
            await Supervisor.ReinitializeAsync();
        });
    }

    /// <summary>Recomputes the working directory and loads its last session. UI thread. True when the directory changed.</summary>
    private bool UpdateWorkingDirectory()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? solutionDirectory = null;
        string? folder = null;
        if (((System.IServiceProvider)Package).GetService(typeof(SVsSolution)) is IVsSolution solution
            && ErrorHandler.Succeeded(solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out var open)) && open is true
            && ErrorHandler.Succeeded(solution.GetSolutionInfo(out var directory, out var file, out _)))
        {
            var folderMode = ErrorHandler.Succeeded(solution.GetProperty((int)__VSPROPID7.VSPROPID_IsInOpenFolderMode, out var mode)) && mode is true;
            if (folderMode)
            {
                folder = directory;
            }
            else if (!string.IsNullOrEmpty(file))
            {
                solutionDirectory = directory;
            }
        }
        var cwd = WorkingDirectory.Resolve(solutionDirectory, folder, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var last = _store.CollectionExists(LastSessionCollection) ? _store.GetString(LastSessionCollection, LastSession.KeyFor(cwd), "") : "";
        bool changed;
        lock (_gate)
        {
            changed = _cwd.Length == 0 || !WorkingDirectory.Same(cwd, _cwd);
            _cwd = cwd;
            _solutionDirectory = string.IsNullOrWhiteSpace(solutionDirectory) ? null : WorkingDirectory.Normalize(solutionDirectory!);
        }
        if (changed)
        {
            _sessions.SwitchTo(cwd, last);
        }

        return changed;
    }

    /// <summary>Offers a restart when settings that shape the OMP process differ from those it runs with; Restart rebuilds it with them.</summary>
    private void OnOptionsApplied(object sender, EventArgs e)
    {
        if (!SettingsChanged())
        {
            return;
        }

        Background.Run(Package.JoinableTaskFactory, Logger, "Applying oh-my-pi settings", async () =>
        {
            var choice = await Notifier.ShowAsync("OMP settings changed. Restart OMP to apply them?", KnownMonikers.StatusInformation, "Restart");
            if (choice != "Restart")
            {
                return;
            }

            try
            {
                await Supervisor.RecreateAsync();
            }
            catch (Exception error)
            {
                Logger.Error("OMP restart after settings change failed", error);
                await Notifier.ShowErrorAsync($"OMP restart failed: {error.Message}");
            }
        });
    }

    /// <summary>Whether the Visual Studio process that owns diff copies still runs; unknown counts as running.</summary>
    private static bool IsRunning(int processId)
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                return !process.HasExited;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The options that require a new OMP process when they change.</summary>
    private readonly struct ProcessSettings : IEquatable<ProcessSettings>
    {
        private ProcessSettings(string executablePath, string extraArgs, bool autoRestart)
        {
            ExecutablePath = executablePath;
            ExtraArgs = extraArgs;
            AutoRestart = autoRestart;
        }

        public string ExecutablePath { get; }

        public string ExtraArgs { get; }

        public bool AutoRestart { get; }

        public static ProcessSettings From(OmpOptionsPage options) => new ProcessSettings(options.ExecutablePath ?? "", options.ExtraArgs ?? "", options.AutoRestart);

        public bool Equals(ProcessSettings other) =>
            ExecutablePath == other.ExecutablePath && ExtraArgs == other.ExtraArgs && AutoRestart == other.AutoRestart;

        public override bool Equals(object? obj) => obj is ProcessSettings other && Equals(other);

        public override int GetHashCode() => (ExecutablePath, ExtraArgs, AutoRestart).GetHashCode();
    }
}
