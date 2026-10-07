using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Omp.Core;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;

namespace OhMyPi.VisualStudio
{
    /// <summary>The OMP commands from <c>OmpPackage.vsct</c>.</summary>
    internal sealed class OmpCommands
    {
        private readonly OmpPackage _package;
        private readonly OmpRuntime _runtime;

        private OmpCommands(OmpPackage package, OmpRuntime runtime)
        {
            _package = package;
            _runtime = runtime;
        }

        public static async Task RegisterAsync(OmpPackage package, OmpRuntime runtime)
        {
            var commandService = await package.GetServiceAsync<IMenuCommandService, OleMenuCommandService>();
            var commands = new OmpCommands(package, runtime);
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            commands.Register(commandService);
        }

        private void Register(OleMenuCommandService commandService)
        {
            void Add(int id, string name, Func<Task> run, EventHandler? queryStatus = null)
            {
                var command = new OleMenuCommand((sender, e) => Execute(name, run), new CommandID(PackageGuids.CommandSet, id));
                if (queryStatus != null) command.BeforeQueryStatus += queryStatus;
                commandService.AddCommand(command);
            }

            Add(PackageIds.Open, "OMP.Open", () => _package.ShowChatAsync(activate: true));
            Add(PackageIds.ViewToolWindow, "View.OhMyPi", () => _package.ShowChatAsync(activate: true));
            Add(PackageIds.NewSession, "OMP.NewSession", NewSessionAsync);
            Add(PackageIds.ResumeSession, "OMP.ResumeSession", () => WithChatAsync(chat => chat.ShowSessionPicker()));
            Add(PackageIds.SelectModel, "OMP.SelectModel", () => WithChatAsync(chat => chat.ShowModelPicker()));
            Add(PackageIds.SelectThinkingLevel, "OMP.SelectThinkingLevel", () => WithChatAsync(chat => chat.ShowThinkingPicker()));
            Add(PackageIds.RenameSession, "OMP.RenameSession", () => WithChatAsync(chat => chat.BeginRename()));
            Add(PackageIds.ToggleFastMode, "OMP.ToggleFastMode", ToggleFastModeAsync);
            Add(PackageIds.SendPrompt, "OMP.SendPrompt", () => OnChatAsync(chat => chat.FocusInput()));
            Add(PackageIds.ShowAgents, "OMP.ShowAgents", () => OnChatAsync(chat => chat.ShowAgents()));
            Add(PackageIds.Abort, "OMP.Abort", AbortAsync, QueryAbort);
            Add(PackageIds.Restart, "OMP.Restart", () => _runtime.Supervisor.RestartAsync());
            Add(PackageIds.ShowLog, "OMP.ShowLog", () =>
            {
                _runtime.Logger.Show();
                return Task.CompletedTask;
            });
            Add(PackageIds.OpenSettings, "OMP.OpenSettings", () =>
            {
                _runtime.OpenSettings();
                return Task.CompletedTask;
            });
            Add(PackageIds.AddActiveFile, "OMP.AddFileToChat", AddActiveFileAsync);
            Add(PackageIds.AddSelectedFiles, "OMP.AddSelectedFilesToChat", AddSelectedFilesAsync);
            Add(PackageIds.ShowUsage, "OMP.ShowUsage", () => WithChatAsync(chat => chat.ShowUsage()));
        }

        /// <summary>Runs a command handler without blocking the UI thread; failures go to the log and an info bar.</summary>
        private void Execute(string name, Func<Task> run)
        {
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await TaskScheduler.Default;
                    await run();
                }
                catch (Exception error)
                {
                    _runtime.Logger.Error($"Command {name} failed", error);
                    try
                    {
                        if (await _runtime.Notifier.ShowErrorAsync($"OMP: {error.Message}", "Show Log") == "Show Log") _runtime.Logger.Show();
                    }
                    catch (Exception notifyError)
                    {
                        _runtime.Logger.Error("Showing the command failure failed", notifyError);
                    }
                }
            });
        }

        private async Task NewSessionAsync()
        {
            await _runtime.Supervisor.NewSessionAsync();
            _runtime.Current?.Host.ClearChanges();
            await _package.ShowChatAsync(activate: true);
        }

        /// <summary>Starts OMP when needed, then reveals the chat and runs a chat action (pickers, rename).</summary>
        private async Task WithChatAsync(Action<OmpChatControl> action)
        {
            await _runtime.Supervisor.EnsureServiceAsync();
            await OnChatAsync(action);
        }

        /// <summary>Reveals the chat and runs <paramref name="action"/> on the UI thread, which owns the control.</summary>
        private async Task OnChatAsync(Action<OmpChatControl> action)
        {
            action(await ChatAsync(activate: true));
        }

        /// <summary>The chat control, once the package has created the first service generation; on the UI thread.</summary>
        private async Task<OmpChatControl> ChatAsync(bool activate)
        {
            await _runtime.Supervisor.WhenInitializedAsync();
            var window = await _package.ShowChatAsync(activate);
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            return window.ShowCurrent() ?? throw new InvalidOperationException("The oh-my-pi chat is not available.");
        }

        private async Task ToggleFastModeAsync()
        {
            var service = await _runtime.Supervisor.EnsureServiceAsync();
            await service.SetFastModeAsync(service.Session.FastModeEnabled != true);
        }

        private Task AbortAsync() => _runtime.Supervisor.Service?.AbortAsync() ?? Task.CompletedTask;

        private void QueryAbort(object sender, EventArgs e)
        {
            var phase = _runtime.Supervisor.Service?.Session.Phase;
            ((OleMenuCommand)sender).Enabled = phase != null && phase != SessionPhase.Idle;
        }

        private async Task AddActiveFileAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            var path = _runtime.ActiveDocument.ActivePath;
            await AddFilesAsync(path == null ? Array.Empty<string>() : new[] { path });
        }

        private async Task AddSelectedFilesAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            await AddFilesAsync(await SelectedItemPathsAsync());
        }

        private async Task AddFilesAsync(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
            {
                await _runtime.Notifier.ShowAsync("Open a file or select files in Solution Explorer to add them to the OMP chat.", KnownMonikers.StatusInformation);
                return;
            }
            (await ChatAsync(activate: false)).AddFileMentions(paths);
        }

        /// <summary>Files and folders selected in Solution Explorer (multi-select included).</summary>
        private async Task<IReadOnlyList<string>> SelectedItemPathsAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            var monitor = await _package.GetServiceAsync<SVsShellMonitorSelection, IVsMonitorSelection>();
            var paths = new List<string>();
            var hierarchyPointer = IntPtr.Zero;
            var containerPointer = IntPtr.Zero;
            try
            {
                ErrorHandler.ThrowOnFailure(monitor.GetCurrentSelection(out hierarchyPointer, out var itemId, out var multiSelect, out containerPointer));
                if (multiSelect != null)
                {
                    ErrorHandler.ThrowOnFailure(multiSelect.GetSelectionInfo(out var count, out _));
                    var items = new VSITEMSELECTION[count];
                    ErrorHandler.ThrowOnFailure(multiSelect.GetSelectedItems(0, count, items));
                    foreach (var item in items) AddItemPath(paths, item.pHier, item.itemid);
                }
                else if (hierarchyPointer != IntPtr.Zero)
                {
                    AddItemPath(paths, (IVsHierarchy)Marshal.GetObjectForIUnknown(hierarchyPointer), itemId);
                }
            }
            finally
            {
                if (hierarchyPointer != IntPtr.Zero) Marshal.Release(hierarchyPointer);
                if (containerPointer != IntPtr.Zero) Marshal.Release(containerPointer);
            }
            return paths;
        }

        private static void AddItemPath(List<string> paths, IVsHierarchy? hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy == null) return;
            string? path = null;
            if (hierarchy is IVsProject project && ErrorHandler.Succeeded(project.GetMkDocument(itemId, out var document))) path = document;
            if (!LocalPaths.Exists(path, allowDirectories: true) && ErrorHandler.Succeeded(hierarchy.GetCanonicalName(itemId, out var canonical))) path = canonical;
            if (LocalPaths.Exists(path, allowDirectories: true) && !paths.Contains(path!)) paths.Add(path!);
        }
    }
}
