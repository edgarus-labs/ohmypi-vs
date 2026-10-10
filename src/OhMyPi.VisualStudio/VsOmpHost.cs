using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio;

/// <summary>The VS services one chat control generation uses; owned together with its OMP service.</summary>
internal sealed class VsOmpHost : IOmpHost, IDismissals, IDisposable
{
    private readonly OmpRuntime _runtime;
    private readonly WorkspaceScope _scope;
    private readonly ChangeTracker _changes;

    /// <param name="scope">The service's working directory and trusted roots, fixed for this generation.</param>
    public VsOmpHost(OmpRuntime runtime, IOmpService service, WorkspaceScope scope, OmpUnavailable? unavailable)
    {
        _runtime = runtime;
        _scope = scope;
        Unavailable = unavailable;
        _changes = new ChangeTracker(service, scope, runtime.Logger, runtime.Package, runtime.ConfirmOutsideWorkspaceAsync);
    }

    public OmpUnavailable? Unavailable { get; }

    public IModelPreferences ModelPreferences => _runtime.ModelPreferences;

    public bool IsDismissed(string key) => _runtime.Dismissals.IsDismissed(key);

    /// <summary>
    /// Dismisses the specified notification or alert identified by the provided key.
    /// </summary>
    /// <param name="key">The key.</param>
    public void Dismiss(string key) => _runtime.Dismissals.Dismiss(key);

    /// <summary>
    /// Gets the active document path.
    /// </summary>
    public string? ActiveDocumentPath
    {
        get
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            return _runtime.ActiveDocument.ActivePath;
        }
    }

    /// <summary>
    /// Occurs when active document changed.
    /// </summary>
    public event EventHandler ActiveDocumentChanged
    {
        add => _runtime.ActiveDocument.Changed += value;
        remove => _runtime.ActiveDocument.Changed -= value;
    }

    /// <summary>
    /// Gets the collection of changes.
    /// </summary>
    public IReadOnlyList<TrackedChange> Changes => _changes.Changes;

    /// <summary>
    /// Occurs when changes changed.
    /// </summary>
    public event EventHandler ChangesChanged
    {
        add => _changes.ChangesChanged += value;
        remove => _changes.ChangesChanged -= value;
    }

    /// <summary>
    /// Asynchronously opens a difference view for the specified path, optionally comparing it against a previously recorded state.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="recordedBefore">The recorded before.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenDiffAsync(string path, string? recordedBefore = null) => _changes.OpenDiffAsync(path, recordedBefore);

    /// <summary>
    /// Asynchronously opens the specified file, optionally navigating to a specific line number.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenFileAsync(string path, int? line = null) => _runtime.OpenFileAsync(_scope, path, line);

    /// <summary>
    /// Displays the runtime logger interface to the user.
    /// </summary>
    public void ShowLog() => _runtime.Logger.Show();

    /// <summary>
    /// Opens the application settings interface via the runtime environment.
    /// </summary>
    public void OpenSettings() => _runtime.OpenSettings();

    /// <summary>
    /// Asynchronously restarts the runtime supervisor to reset the system state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RestartAsync()
    {
        await TaskScheduler.Default;
        await _runtime.Supervisor.RestartAsync();
    }

    /// <summary>
    /// Asynchronously ensures that the underlying runtime supervisor service is initialized and operational.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EnsureServiceAsync()
    {
        await TaskScheduler.Default;
        await _runtime.Supervisor.EnsureServiceAsync();
    }

    /// <summary>
    /// Logs a specified error message and its associated exception to the runtime logging system.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void LogError(string message, Exception error) => _runtime.Logger.Error(message, error);

    /// <summary>
    /// Removes all tracked changes from the internal collection.
    /// </summary>
    public void ClearChanges() => _changes.Clear();

    /// <summary>
    /// Releases the unmanaged resources used by the underlying changes collection.
    /// </summary>
    public void Dispose() => _changes.Dispose();
}
