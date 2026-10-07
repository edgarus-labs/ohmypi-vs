using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Tests;

internal sealed class FakeHost : IOmpHost
{
    public string? ActiveDocumentPath { get; private set; }

    public event EventHandler? ActiveDocumentChanged;

    private IReadOnlyList<TrackedChange> _changes = Array.Empty<TrackedChange>();

    public IReadOnlyList<TrackedChange> Changes
    {
        get => ChangesError is not null ? throw ChangesError : _changes;
        set => _changes = value;
    }

    public Exception? ChangesError { get; set; }

    /// <summary>When set, <see cref="EnsureServiceAsync"/> completes only with this task.</summary>
    public TaskCompletionSource<bool>? EnsureGate { get; set; }

    public Exception? EnsureError { get; set; }

    /// <summary>Handlers currently attached to any of this host's events.</summary>
    public int HandlerCount => (ActiveDocumentChanged?.GetInvocationList().Length ?? 0) + (ChangesChanged?.GetInvocationList().Length ?? 0);

    public event EventHandler? ChangesChanged;

    public List<string> Diffs { get; } = new List<string>();

    public List<(string Path, int? Line)> Opened { get; } = new List<(string, int?)>();

    public OmpUnavailable? Unavailable { get; set; }

    public FakeModelPreferences Preferences { get; } = new FakeModelPreferences();

    public IModelPreferences ModelPreferences => Preferences;

    public void RaiseChanges() => ChangesChanged?.Invoke(this, EventArgs.Empty);

    public void SetActiveDocument(string? path)
    {
        ActiveDocumentPath = path;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public List<(string Path, string? RecordedBefore)> DiffRequests { get; } = new List<(string, string?)>();

    public List<(string Message, Exception Error)> Errors { get; } = new List<(string, Exception)>();

    public int EnsureCalls { get; private set; }

    public Task OpenDiffAsync(string path, string? recordedBefore = null)
    {
        Diffs.Add(path);
        DiffRequests.Add((path, recordedBefore));

        return Task.CompletedTask;
    }

    public Task EnsureServiceAsync()
    {
        EnsureCalls++;
        if (EnsureError is not null)
        {
            return Task.FromException(EnsureError);
        }

        return EnsureGate?.Task ?? Task.CompletedTask;
    }

    public void LogError(string message, Exception error) => Errors.Add((message, error));

    public Task OpenFileAsync(string path, int? line = null)
    {
        Opened.Add((path, line));

        return Task.CompletedTask;
    }

    public void ShowLog() { }

    public void OpenSettings() { }

    public Task RestartAsync() => Task.CompletedTask;
}
