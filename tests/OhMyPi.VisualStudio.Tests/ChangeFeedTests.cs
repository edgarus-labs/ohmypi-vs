using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.Logic;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Tests;

public sealed class ChangeFeedTests : IDisposable
{
    /// <summary>
    /// Represents a no-op implementation of the IOmpLogger interface that suppresses all logging output.
    /// </summary>
    private sealed class NullLogger : IOmpLogger
    {
        /// <summary>
        /// The errors.
        /// </summary>
        public readonly ConcurrentQueue<string> Errors = new();

        /// <summary>
        /// Adds a specified error message and an optional exception to the internal error queue.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Error(string message, Exception? error = null) => Errors.Enqueue(message);

        /// <summary>
        /// Logs a warning message and an optional associated exception to the system diagnostic logs.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Warn(string message, Exception? error = null) { }

        /// <summary>
        /// Logs an informational message and an optional exception to the system diagnostic output.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Info(string message, Exception? error = null) { }

        /// <summary>
        /// Logs a debug message and an optional exception to the diagnostic output.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Debug(string message, Exception? error = null) { }

        /// <summary>
        /// Gets a value indicating whether trace enabled.
        /// </summary>
        public bool TraceEnabled => false;

        /// <summary>
        /// Records a diagnostic trace entry specifying the communication direction and the current execution frame.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="frame">The frame.</param>
        public void Trace(string direction, string frame) { }
    }

    /// <summary>
    /// The cwd.
    /// </summary>
    private const string Cwd = @"D:\work\repo";
    private readonly FakeOmpService _service = new() { Cwd = Cwd };
    private readonly ConcurrentDictionary<string, Snapshot> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly NullLogger _logger = new();
    private Func<string, Task>? _beforeRead;
    private readonly ChangeFeed _feed;
    private int _changed;
    private int _cleared;

    public ChangeFeedTests()
    {
        var model = new ChangeModel(async path =>
        {
            var snapshot = _files.TryGetValue(path, out var found) ? found : Snapshot.Missing;
            if (_beforeRead is not null)
            {
                await _beforeRead(path);
            }

            return snapshot;
        }, _logger);
        _feed = new ChangeFeed(_service, model, new WorkspaceScope(Cwd, new[] { Cwd }), _logger);
        _feed.ChangesChanged += (_, _) => System.Threading.Interlocked.Increment(ref _changed);
        _feed.Cleared += (_, _) => System.Threading.Interlocked.Increment(ref _cleared);
    }

    public void Dispose() => _feed.Dispose();

    private static async Task Within(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))));
        await task;
    }

    private static ToolExecutionEvent Write(ToolExecutionPhase phase, string id, string path) => new()
    {
        Phase = phase,
        ToolCallId = id,
        Name = "write",
        Args = new JObject { ["path"] = path, ["content"] = "new" },
        Result = phase == ToolExecutionPhase.End ? new ToolResultView { Text = "ok" } : null,
    };

    private void RunWrite(string id, string relative, string? before, string? after)
    {
        var path = System.IO.Path.Combine(Cwd, relative);
        _files[path] = before is null ? Snapshot.Missing : Snapshot.Of(before);
        _service.RaiseToolExecution(Write(ToolExecutionPhase.Start, id, relative));
        _feed.WhenIdleAsync().GetAwaiter().GetResult();
        _files[path] = after is null ? Snapshot.Missing : Snapshot.Of(after);
        _service.RaiseToolExecution(Write(ToolExecutionPhase.End, id, relative));
    }

    [Fact]
    public async Task PublishesChangesFromToolEvents()
    {
        RunWrite("t1", "a.txt", null, "hello\n");
        await _feed.WhenIdleAsync();

        var change = Assert.Single(_feed.Changes);
        Assert.Equal(@"D:\work\repo\a.txt", change.Path, ignoreCase: true);
        Assert.Equal(ChangeStatus.Added, change.Status);
        Assert.True(_changed > 0);
        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task SortsChangesByPath()
    {
        RunWrite("t1", "b.txt", null, "b");
        RunWrite("t2", "a.txt", "x", "y");
        await _feed.WhenIdleAsync();

        Assert.Equal(new[] { "a.txt", "b.txt" }, _feed.Changes.Select(c => System.IO.Path.GetFileName(c.Path)));
    }

    [Fact]
    public async Task AppliesEventsOneAtATimeInArrivalOrder()
    {
        var path = System.IO.Path.Combine(Cwd, "a.txt");
        _files[path] = Snapshot.Of("old");
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _beforeRead = async _ =>
        {
            _beforeRead = null;
            held.TrySetResult(true);
            await release.Task;
        };

        _service.RaiseToolExecution(Write(ToolExecutionPhase.Start, "t1", "a.txt"));
        await Within(held.Task);
        _files[path] = Snapshot.Of("new");
        _service.RaiseToolExecution(Write(ToolExecutionPhase.End, "t1", "a.txt"));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_feed.Changes);

        release.SetResult(true);
        await _feed.WhenIdleAsync();
        Assert.Equal(ChangeStatus.Modified, Assert.Single(_feed.Changes).Status);
    }

    [Fact]
    public async Task ClearsWhenTheSessionSwitchesButNotOnTheFirstSession()
    {
        _service.SetSession(new SessionView { SessionId = "s1" });
        RunWrite("t1", "a.txt", null, "a");
        await _feed.WhenIdleAsync();
        _service.SetSession(new SessionView { SessionId = "s1", SessionName = "renamed" });
        await _feed.WhenIdleAsync();
        Assert.Single(_feed.Changes);
        Assert.Equal(0, _cleared);

        _service.SetSession(new SessionView { SessionId = "s2" });
        await _feed.WhenIdleAsync();

        Assert.Empty(_feed.Changes);
        Assert.Equal(1, _cleared);
    }

    [Fact]
    public async Task ClearForgetsEverything()
    {
        RunWrite("t1", "a.txt", "x", "y");
        await _feed.WhenIdleAsync();
        _feed.Clear();
        await _feed.WhenIdleAsync();

        Assert.Empty(_feed.Changes);
        Assert.Equal(1, _cleared);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "a.txt"), null));
        Assert.Equal(@"No diff recorded for D:\work\repo\a.txt", error.Message);
    }

    [Fact]
    public async Task DiffSourceIsTheFirstSeenBaseline()
    {
        RunWrite("t1", "a.txt", "one\n", "two\n");
        RunWrite("t2", "a.txt", "two\n", "three\n");
        RunWrite("t3", "gone.txt", "bye\n", null);
        RunWrite("t4", "new.txt", null, "hi\n");
        await _feed.WhenIdleAsync();

        Assert.Equal(new DiffSource(false, "one\n"), await _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "a.txt"), null));
        Assert.Equal(new DiffSource(true, "bye\n"), await _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "gone.txt"), null));
        Assert.Equal(new DiffSource(false, ""), await _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "new.txt"), null));
    }

    [Fact]
    public async Task TheTrackedBaselineWinsOverTheRecordedBefore()
    {
        RunWrite("t1", "a.txt", "one\n", "two\n");
        await _feed.WhenIdleAsync();

        Assert.Equal(new DiffSource(false, "one\n"), await _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "a.txt"), "recorded\n"));
    }

    [Fact]
    public async Task AnUntrackedFileDiffsAgainstTheRecordedBefore() => Assert.Equal(new DiffSource(false, "recorded\n"), await _feed.DiffSourceAsync(System.IO.Path.Combine(Cwd, "elsewhere.txt"), "recorded\n"));

    [Fact]
    public async Task AnUntrackedFileWithoutRecordedBeforeHasNoDiff()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _feed.DiffSourceAsync(@"D:\work\repo\never.txt", null));
        Assert.Equal(@"No diff recorded for D:\work\repo\never.txt", error.Message);
    }

    [Fact]
    public async Task StopsListeningWhenDisposed()
    {
        _feed.Dispose();
        RunWrite("t1", "a.txt", null, "a");
        await _feed.WhenIdleAsync();
        Assert.Empty(_feed.Changes);
    }
}
