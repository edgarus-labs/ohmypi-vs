using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>What the diff window compares: the first-seen content and whether the file is gone now.</summary>
internal readonly struct DiffSource : IEquatable<DiffSource>
{
    public DiffSource(bool deleted, string before)
    {
        Deleted = deleted;
        Before = before;
    }

    public bool Deleted { get; }

    public string Before { get; }

    public bool Equals(DiffSource other) => Deleted == other.Deleted && Before == other.Before;

    public override bool Equals(object? obj) => obj is DiffSource other && Equals(other);

    public override int GetHashCode() => (Deleted, Before).GetHashCode();

    public override string ToString() => $"Deleted={Deleted}, Before={Before}";
}

/// <summary>
/// Feeds OMP tool events into a <see cref="ChangeModel"/> one at a time in arrival order and publishes the
/// tracked changes. A new or switched session clears everything; the first session of the service does not.
/// </summary>
internal sealed class ChangeFeed : IDisposable
{
    private readonly IOmpService _service;
    private readonly ChangeModel _model;
    private readonly ChangeScope _scope;
    private readonly IOmpLogger _logger;
    private readonly object _gate = new object();
    private Task _tail = Task.CompletedTask;
    private string? _sessionId;
    private volatile IReadOnlyList<TrackedChange> _changes = Array.Empty<TrackedChange>();
    private bool _disposed;

    /// <param name="scope">Where the service's tool paths resolve and which files may be tracked.</param>
    public ChangeFeed(IOmpService service, ChangeModel model, WorkspaceScope scope, IOmpLogger logger)
    {
        _service = service;
        _model = model;
        _scope = new ChangeScope { Cwd = scope.Cwd, Roots = scope.Roots };
        _logger = logger;
        _sessionId = service.Session.SessionId;
        service.ToolExecution += OnToolExecution;
        service.SessionChanged += OnSessionChanged;
    }

    /// <summary>Raised on a background thread after <see cref="Changes"/> changed.</summary>
    public event EventHandler? ChangesChanged;

    /// <summary>Raised on a background thread after everything was forgotten; baselines shown in diffs are gone.</summary>
    public event EventHandler? Cleared;

    /// <summary>Snapshot sorted by path; safe to read from any thread.</summary>
    public IReadOnlyList<TrackedChange> Changes => _changes;

    public void Clear() => _ = EnqueueAsync(() =>
                                {
                                    _model.Clear();
                                    Publish();
                                    Cleared?.Invoke(this, EventArgs.Empty);

                                    return Task.FromResult(true);
                                });

    /// <summary>
    /// What the diff of <paramref name="path"/> compares: its tracked first-seen content when there is one (empty when
    /// the file did not exist), else <paramref name="recordedBefore"/>, the content a tool reported before it ran.
    /// </summary>
    /// <exception cref="InvalidOperationException">Neither exists, so there is nothing truthful to compare.</exception>
    public Task<DiffSource> DiffSourceAsync(string path, string? recordedBefore) => EnqueueAsync(() =>
    {
        var deleted = _model.Change(path)?.Status == ChangeStatus.Deleted;
        var baseline = _model.Baseline(path);
        if (baseline is not null)
        {
            return Task.FromResult(new DiffSource(deleted, baseline.Content ?? ""));
        }

        if (recordedBefore is not null)
        {
            return Task.FromResult(new DiffSource(deleted, recordedBefore));
        }

        throw new InvalidOperationException($"No diff recorded for {path}");
    });

    /// <summary>Completes once every event received so far has been processed.</summary>
    public Task WhenIdleAsync()
    {
#pragma warning disable VSTHRD003 // The tail is this feed's own queue, never work started by the caller's thread.
        lock (_gate)
        {
            return _tail;
        }
#pragma warning restore VSTHRD003
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        _service.ToolExecution -= OnToolExecution;
        _service.SessionChanged -= OnSessionChanged;
    }

    private void OnToolExecution(object sender, ToolExecutionEvent e) => _ = EnqueueAsync(async () =>
                                                                              {
                                                                                  try
                                                                                  {
                                                                                      var result = await _model.ApplyAsync(e, _scope).ConfigureAwait(false);
                                                                                      if (result.ChangesChanged)
                                                                                      {
                                                                                          Publish();
                                                                                      }
                                                                                  }
                                                                                  catch (Exception error)
                                                                                  {
                                                                                      _logger.Error("Change tracking failed", error);
                                                                                  }

                                                                                  return true;
                                                                              });

    private void OnSessionChanged(object sender, SessionView session)
    {
        lock (_gate)
        {
            if (session.SessionId == _sessionId)
            {
                return;
            }

            var hadSession = _sessionId is not null;
            _sessionId = session.SessionId;
            if (!hadSession)
            {
                return;
            }
        }
        Clear();
    }

    private void Publish()
    {
        _changes = _model.Changes.OrderBy(change => change.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        ChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="work"/> after everything queued before it.</summary>
    private Task<T> EnqueueAsync<T>(Func<Task<T>> work)
    {
        lock (_gate)
        {
            var run = _tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            _tail = run.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return run;
        }
    }
}
