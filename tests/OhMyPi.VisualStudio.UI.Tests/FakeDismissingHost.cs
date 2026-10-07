using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>A host that remembers closed items, as Visual Studio's host does.</summary>
internal sealed class FakeDismissingHost : IOmpHost, IDismissals
{
    private readonly FakeHost _host = new FakeHost();

    public HashSet<string> Dismissed { get; } = new HashSet<string>();

    public Exception? DismissError { get; set; }

    public List<(string Message, Exception Error)> Errors => _host.Errors;

    public bool IsDismissed(string key) => Dismissed.Contains(key);

    public void Dismiss(string key)
    {
        if (DismissError is not null)
        {
            throw DismissError;
        }

        Dismissed.Add(key);
    }

    public string? ActiveDocumentPath => _host.ActiveDocumentPath;

    public event EventHandler? ActiveDocumentChanged { add => _host.ActiveDocumentChanged += value; remove => _host.ActiveDocumentChanged -= value; }

    public IReadOnlyList<TrackedChange> Changes => _host.Changes;

    public event EventHandler? ChangesChanged { add => _host.ChangesChanged += value; remove => _host.ChangesChanged -= value; }

    public OmpUnavailable? Unavailable => _host.Unavailable;

    public IModelPreferences ModelPreferences => _host.ModelPreferences;

    public Task OpenDiffAsync(string path, string? recordedBefore = null) => _host.OpenDiffAsync(path, recordedBefore);

    public Task EnsureServiceAsync() => _host.EnsureServiceAsync();

    public void LogError(string message, Exception error) => _host.LogError(message, error);

    public Task OpenFileAsync(string path, int? line = null) => _host.OpenFileAsync(path, line);

    public void ShowLog() => _host.ShowLog();

    public void OpenSettings() => _host.OpenSettings();

    public Task RestartAsync() => _host.RestartAsync();
}
