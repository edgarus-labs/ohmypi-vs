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

    public void Dismiss(string key) => _runtime.Dismissals.Dismiss(key);

    public string? ActiveDocumentPath
    {
        get
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

            return _runtime.ActiveDocument.ActivePath;
        }
    }

    public event EventHandler ActiveDocumentChanged
    {
        add => _runtime.ActiveDocument.Changed += value;
        remove => _runtime.ActiveDocument.Changed -= value;
    }

    public IReadOnlyList<TrackedChange> Changes => _changes.Changes;

    public event EventHandler ChangesChanged
    {
        add => _changes.ChangesChanged += value;
        remove => _changes.ChangesChanged -= value;
    }

    public Task OpenDiffAsync(string path, string? recordedBefore = null) => _changes.OpenDiffAsync(path, recordedBefore);

    public Task OpenFileAsync(string path, int? line = null) => _runtime.OpenFileAsync(_scope, path, line);

    public void ShowLog() => _runtime.Logger.Show();

    public void OpenSettings() => _runtime.OpenSettings();

    public async Task RestartAsync()
    {
        await TaskScheduler.Default;
        await _runtime.Supervisor.RestartAsync();
    }

    public async Task EnsureServiceAsync()
    {
        await TaskScheduler.Default;
        await _runtime.Supervisor.EnsureServiceAsync();
    }

    public void LogError(string message, Exception error) => _runtime.Logger.Error(message, error);

    public void ClearChanges() => _changes.Clear();

    public void Dispose() => _changes.Dispose();
}
