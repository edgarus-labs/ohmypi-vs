using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio
{
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

        public override IVsAsyncToolWindowFactory? GetAsyncToolWindowFactory(Guid toolWindowType) =>
            toolWindowType == PackageGuids.ToolWindow ? this : null;

        protected override string? GetToolWindowTitle(Type toolWindowType, int id) =>
            toolWindowType == typeof(OmpToolWindow) ? "oh-my-pi" : base.GetToolWindowTitle(toolWindowType, id);

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
}
