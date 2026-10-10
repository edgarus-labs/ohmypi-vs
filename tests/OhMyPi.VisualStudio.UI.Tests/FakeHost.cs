using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>
/// Provides a mock implementation of the IOmpHost interface for testing host operations, document tracking, and service lifecycle management.
/// </summary>
internal sealed class FakeHost : IOmpHost
{
    /// <summary>
    /// Gets or sets the active document path.
    /// </summary>
    public string? ActiveDocumentPath { get; private set; }

    /// <summary>
    /// Occurs when active document changed.
    /// </summary>
    public event EventHandler? ActiveDocumentChanged;

    private IReadOnlyList<TrackedChange> _changes = Array.Empty<TrackedChange>();

    /// <summary>
    /// Gets or sets the collection of changes.
    /// </summary>
    public IReadOnlyList<TrackedChange> Changes
    {
        get => ChangesError is not null ? throw ChangesError : _changes;
        set => _changes = value;
    }

    /// <summary>
    /// Gets or sets the changes error.
    /// </summary>
    public Exception? ChangesError { get; set; }

    /// <summary>When set, <see cref="EnsureServiceAsync"/> completes only with this task.</summary>
    public TaskCompletionSource<bool>? EnsureGate { get; set; }

    /// <summary>
    /// Gets or sets the ensure error.
    /// </summary>
    public Exception? EnsureError { get; set; }

    /// <summary>Handlers currently attached to any of this host's events.</summary>
    public int HandlerCount => (ActiveDocumentChanged?.GetInvocationList().Length ?? 0) + (ChangesChanged?.GetInvocationList().Length ?? 0);

    /// <summary>
    /// Occurs when changes changed.
    /// </summary>
    public event EventHandler? ChangesChanged;

    /// <summary>
    /// Gets the collection of diffs.
    /// </summary>
    public List<string> Diffs { get; } = new List<string>();

    /// <summary>
    /// Gets the collection of opened.
    /// </summary>
    public List<(string Path, int? Line)> Opened { get; } = new List<(string, int?)>();

    /// <summary>
    /// Gets or sets the unavailable.
    /// </summary>
    public OmpUnavailable? Unavailable { get; set; }

    /// <summary>
    /// Gets the preferences.
    /// </summary>
    public FakeModelPreferences Preferences { get; } = new FakeModelPreferences();

    /// <summary>
    /// Gets the model preferences.
    /// </summary>
    public IModelPreferences ModelPreferences => Preferences;

    /// <summary>
    /// Notifies subscribers that changes have occurred by invoking the ChangesChanged event.
    /// </summary>
    public void RaiseChanges() => ChangesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Sets the path of the active document and notifies subscribers that the active document has changed.
    /// </summary>
    /// <param name="path">The path.</param>
    public void SetActiveDocument(string? path)
    {
        ActiveDocumentPath = path;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the collection of diff requests.
    /// </summary>
    public List<(string Path, string? RecordedBefore)> DiffRequests { get; } = new List<(string, string?)>();

    /// <summary>
    /// Gets the collection of errors.
    /// </summary>
    public List<(string Message, Exception Error)> Errors { get; } = new List<(string, Exception)>();

    /// <summary>
    /// Gets or sets the ensure calls.
    /// </summary>
    public int EnsureCalls { get; private set; }

    /// <summary>
    /// Asynchronously queues a request to open a difference view for the specified path, optionally relative to a previously recorded state.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="recordedBefore">The recorded before.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenDiffAsync(string path, string? recordedBefore = null)
    {
        Diffs.Add(path);
        DiffRequests.Add((path, recordedBefore));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously ensures that the service is initialized, returning a faulted task if a previous initialization error occurred or awaiting the completion of the initialization gate.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task EnsureServiceAsync()
    {
        EnsureCalls++;
        if (EnsureError is not null)
        {
            return Task.FromException(EnsureError);
        }

        return EnsureGate?.Task ?? Task.CompletedTask;
    }

    /// <summary>
    /// Records a specified error message and its associated exception to the internal error collection.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void LogError(string message, Exception error) => Errors.Add((message, error));

    /// <summary>
    /// Asynchronously records the specified file path and optional line number to the collection of opened files.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenFileAsync(string path, int? line = null)
    {
        Opened.Add((path, line));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Displays the application log entries to the user.
    /// </summary>
    public void ShowLog() { }

    /// <summary>
    /// Opens the application settings configuration interface.
    /// </summary>
    public void OpenSettings() { }

    /// <summary>
    /// Asynchronously restarts the service or component to restore its operational state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RestartAsync() => Task.CompletedTask;
}
