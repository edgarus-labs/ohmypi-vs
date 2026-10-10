using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio;

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
            if (queryStatus is not null)
            {
                command.BeforeQueryStatus += queryStatus;
            }

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
    private void Execute(string name, Func<Task> run) => _ = _package.JoinableTaskFactory.RunAsync(async () =>
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
                                                                          if (await _runtime.Notifier.ShowErrorAsync($"OMP: {error.Message}", "Show Log") == "Show Log")
                                                                          {
                                                                              _runtime.Logger.Show();
                                                                          }
                                                                      }
                                                                      catch (Exception notifyError)
                                                                      {
                                                                          _runtime.Logger.Error("Showing the command failure failed", notifyError);
                                                                      }
                                                                  }
                                                              });

    /// <summary>
    /// Asynchronously initializes a new session, clears pending host changes, and displays the chat interface.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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
    private async Task OnChatAsync(Action<OmpChatControl> action) => action(await ChatAsync(activate: true));

    /// <summary>The chat control, once the package has created the first service generation; on the UI thread.</summary>
    private async Task<OmpChatControl> ChatAsync(bool activate)
    {
        await _runtime.Supervisor.WhenInitializedAsync();
        var window = await _package.ShowChatAsync(activate);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        return window.ShowCurrent() ?? throw new InvalidOperationException("The oh-my-pi chat is not available.");
    }

    /// <summary>
    /// Asynchronously toggles the fast mode setting of the current session via the runtime supervisor service.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ToggleFastModeAsync()
    {
        var service = await _runtime.Supervisor.EnsureServiceAsync();
        await service.SetFastModeAsync(service.Session.FastModeEnabled != true);
    }

    /// <summary>
    /// Asynchronously requests the termination of the underlying runtime supervisor service if it is currently available.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task AbortAsync() => _runtime.Supervisor.Service?.AbortAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Updates the enabled state of the query abort menu command based on whether the current session phase is active and not idle.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The e.</param>
    private void QueryAbort(object sender, EventArgs e)
    {
        var phase = _runtime.Supervisor.Service?.Session.Phase;
        ((OleMenuCommand)sender).Enabled = phase is not null && phase != SessionPhase.Idle;
    }

    /// <summary>
    /// Asynchronously adds the currently active document&apos;s file path to the package.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddActiveFileAsync()
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var path = _runtime.ActiveDocument.ActivePath;
        await AddFilesAsync(path is null ? [] : new[] { path });
    }

    /// <summary>
    /// Asynchronously retrieves the currently selected file paths and adds them to the package on the main thread.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddSelectedFilesAsync()
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        await AddFilesAsync(await SelectedItemPathsAsync());
    }

    /// <summary>
    /// Asynchronously adds the specified file paths as mentions to the OMP chat or notifies the user if no paths are provided.
    /// </summary>
    /// <param name="paths">The collection of paths.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
            if (multiSelect is not null)
            {
                ErrorHandler.ThrowOnFailure(multiSelect.GetSelectionInfo(out var count, out _));
                var items = new VSITEMSELECTION[count];
                ErrorHandler.ThrowOnFailure(multiSelect.GetSelectedItems(0, count, items));
                foreach (var item in items)
                {
                    AddItemPath(paths, item.pHier, item.itemid);
                }
            }
            else if (hierarchyPointer != IntPtr.Zero)
            {
                AddItemPath(paths, (IVsHierarchy)Marshal.GetObjectForIUnknown(hierarchyPointer), itemId);
            }
        }
        finally
        {
            if (hierarchyPointer != IntPtr.Zero)
            {
                Marshal.Release(hierarchyPointer);
            }

            if (containerPointer != IntPtr.Zero)
            {
                Marshal.Release(containerPointer);
            }
        }

        return paths;
    }

    /// <summary>
    /// Resolves the file system path for a specified hierarchy item and adds it to the provided list if the path exists and is not already present.
    /// </summary>
    /// <param name="paths">The collection of paths.</param>
    /// <param name="hierarchy">The hierarchy.</param>
    /// <param name="itemId">The unique identifier of the item.</param>
    private static void AddItemPath(List<string> paths, IVsHierarchy? hierarchy, uint itemId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (hierarchy is null)
        {
            return;
        }

        string? path = null;
        if (hierarchy is IVsProject project && ErrorHandler.Succeeded(project.GetMkDocument(itemId, out var document)))
        {
            path = document;
        }

        if (!LocalPaths.Exists(path, allowDirectories: true) && ErrorHandler.Succeeded(hierarchy.GetCanonicalName(itemId, out var canonical)))
        {
            path = canonical;
        }

        if (LocalPaths.Exists(path, allowDirectories: true) && !paths.Contains(path!))
        {
            paths.Add(path!);
        }
    }
}
