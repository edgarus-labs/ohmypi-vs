using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>A host that remembers closed items, as Visual Studio's host does.</summary>
internal sealed class FakeDismissingHost : IOmpHost, IDismissals
{
    private readonly FakeHost _host = new FakeHost();

    /// <summary>
    /// Gets the dismissed.
    /// </summary>
    public HashSet<string> Dismissed { get; } = new HashSet<string>();

    /// <summary>
    /// Gets or sets the dismiss error.
    /// </summary>
    public Exception? DismissError { get; set; }

    /// <summary>
    /// Gets the collection of errors.
    /// </summary>
    public List<(string Message, Exception Error)> Errors => _host.Errors;

    /// <summary>
    /// Determines whether the specified key exists within the collection of dismissed items.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    public bool IsDismissed(string key) => Dismissed.Contains(key);

    /// <summary>
    /// Marks the specified key as dismissed, provided no dismissal error is currently pending.
    /// </summary>
    /// <param name="key">The key.</param>
    public void Dismiss(string key)
    {
        if (DismissError is not null)
        {
            throw DismissError;
        }

        Dismissed.Add(key);
    }

    /// <summary>
    /// Gets the active document path.
    /// </summary>
    public string? ActiveDocumentPath => _host.ActiveDocumentPath;

    /// <summary>
    /// Occurs when active document changed.
    /// </summary>
    public event EventHandler? ActiveDocumentChanged { add => _host.ActiveDocumentChanged += value; remove => _host.ActiveDocumentChanged -= value; }

    /// <summary>
    /// Gets the collection of changes.
    /// </summary>
    public IReadOnlyList<TrackedChange> Changes => _host.Changes;

    /// <summary>
    /// Occurs when changes changed.
    /// </summary>
    public event EventHandler? ChangesChanged { add => _host.ChangesChanged += value; remove => _host.ChangesChanged -= value; }

    /// <summary>
    /// Gets the unavailable.
    /// </summary>
    public OmpUnavailable? Unavailable => _host.Unavailable;

    /// <summary>
    /// Gets the model preferences.
    /// </summary>
    public IModelPreferences ModelPreferences => _host.ModelPreferences;

    /// <summary>
    /// Asynchronously opens a difference view between the specified path and an optional previously recorded state.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="recordedBefore">The recorded before.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenDiffAsync(string path, string? recordedBefore = null) => _host.OpenDiffAsync(path, recordedBefore);

    /// <summary>
    /// Asynchronously ensures that the underlying service is initialized and running.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task EnsureServiceAsync() => _host.EnsureServiceAsync();

    /// <summary>
    /// Logs a specified error message and the associated exception to the host logging system.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="error">The error.</param>
    public void LogError(string message, Exception error) => _host.LogError(message, error);

    /// <summary>
    /// Asynchronously opens the specified file, optionally navigating to a specific line number.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task OpenFileAsync(string path, int? line = null) => _host.OpenFileAsync(path, line);

    /// <summary>
    /// Displays the application log window via the host.
    /// </summary>
    public void ShowLog() => _host.ShowLog();

    /// <summary>
    /// Opens the application settings interface via the host.
    /// </summary>
    public void OpenSettings() => _host.OpenSettings();

    /// <summary>
    /// Asynchronously restarts the host instance.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RestartAsync() => _host.RestartAsync();
}
