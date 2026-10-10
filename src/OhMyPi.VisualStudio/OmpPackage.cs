using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio;

/// <summary>
/// oh-my-pi for Visual Studio: a frontend for the OMP coding-agent runtime (<c>omp --mode rpc-ui</c>).
/// Loads in the background when a command runs or the tool window is restored.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("oh-my-pi", "Visual Studio client for the oh-my-pi (OMP) coding-agent runtime.", VersionInfo.Version)]
[ProvideMenuResource("Menus.ctmenu", 3)]
[ProvideToolWindow(typeof(OmpToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer, Orientation = ToolWindowOrientation.Right, Width = 420, Height = 700)]
[ProvideOptionPage(typeof(OmpOptionsPage), "oh-my-pi", "General", 0, 0, true)]
[Guid(PackageGuids.PackageString)]
public sealed class OmpPackage : AsyncPackage
{
    private OmpRuntime? _runtime;

    /// <summary>
    /// Asynchronously initializes the OMP runtime by configuring options, registering commands, and starting the background execution process on the main thread.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <param name="progress">The progress.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var options = (OmpOptionsPage)GetDialogPage(typeof(OmpOptionsPage));
        var runtime = await OmpRuntime.CreateAsync(this, options);
        _runtime = runtime;
        await OmpCommands.RegisterAsync(this, runtime);
        runtime.Initialize();
        Background.Run(JoinableTaskFactory, runtime.Logger, "Starting OMP", runtime.StartAsync);
    }

    /// <summary>
    /// Retrieves the asynchronous tool window factory associated with the specified tool window type if it matches the package&apos;s defined tool window identifier.
    /// </summary>
    /// <param name="toolWindowType">The tool window type.</param>
    /// <returns>The ivs async tool window factory? result.</returns>
    public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
        toolWindowType == PackageGuids.ToolWindow ? this : null;

    /// <summary>
    /// Retrieves the title of the specified tool window, returning a custom title for the OmpToolWindow type or the base implementation&apos;s title otherwise.
    /// </summary>
    /// <param name="toolWindowType">The tool window type.</param>
    /// <param name="id">The unique identifier.</param>
    /// <returns>The string? result.</returns>
    protected override string? GetToolWindowTitle(Type toolWindowType, int id) =>
        toolWindowType == typeof(OmpToolWindow) ? "oh-my-pi" : base.GetToolWindowTitle(toolWindowType, id);

    /// <summary>
    /// Asynchronously initializes the specified tool window by returning the current runtime instance.
    /// </summary>
    /// <param name="toolWindowType">The tool window type.</param>
    /// <param name="id">The unique identifier.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>The system.threading.tasks.task result.</returns>
    protected override System.Threading.Tasks.Task<object?> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken) =>
        System.Threading.Tasks.Task.FromResult<object?>(_runtime);

    /// <summary>Shows the chat tool window, creating it when needed; <paramref name="activate"/> moves focus to it.</summary>
    internal async System.Threading.Tasks.Task<OmpToolWindow> ShowChatAsync(bool activate)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var pane = (OmpToolWindow)await FindToolWindowAsync(typeof(OmpToolWindow), 0, true, DisposalToken);
        var frame = (IVsWindowFrame)pane.Frame;
        ErrorHandler.ThrowOnFailure(activate ? frame.Show() : frame.ShowNoActivate());

        return pane;
    }

    /// <summary>Whether the chat tool window exists and is visible on screen (not a hidden tab).</summary>
    internal async System.Threading.Tasks.Task<bool> IsChatOnScreenAsync()
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
        var pane = await FindToolWindowAsync(typeof(OmpToolWindow), 0, false, DisposalToken);

        return pane?.Frame is IVsWindowFrame frame && ErrorHandler.Succeeded(frame.IsOnScreen(out var onScreen)) && onScreen != 0;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the current object and disposes of the runtime instance if the operation is triggered by a managed disposal.
    /// </summary>
    /// <param name="disposing">The disposing.</param>
    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing)
        {
            _runtime?.Dispose();
            _runtime = null;
        }
        base.Dispose(disposing);
    }
}
