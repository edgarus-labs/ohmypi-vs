using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.Logic;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Tests;

public sealed class FailingTrackingTests : IDisposable
{
    /// <summary>
    /// Provides a concrete implementation of the IOmpLogger interface for recording diagnostic messages and tracking errors across various severity levels.
    /// </summary>
    private sealed class Logger : IOmpLogger
    {
        /// <summary>
        /// The errors.
        /// </summary>
        public readonly ConcurrentQueue<string> Errors = new();

        /// <summary>
        /// Enqueues a specified error message and an optional exception into the error collection.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Error(string message, Exception? error = null) => Errors.Enqueue(message);

        /// <summary>
        /// Logs a warning message and an optional exception to the system diagnostic logs.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="error">The error.</param>
        public void Warn(string message, Exception? error = null) { }

        /// <summary>
        /// Logs an informational message and an optional exception to the system diagnostic log.
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
    private readonly Logger _logger = new();
    private readonly ChangeFeed _feed;

    public FailingTrackingTests()
    {
        var model = new ChangeModel(_ => throw new InvalidOperationException("disk gone"), _logger);
        _feed = new ChangeFeed(_service, model, new WorkspaceScope(Cwd, new[] { Cwd }), _logger);
    }

    public void Dispose() => _feed.Dispose();

    [Fact]
    public async Task AToolEventThatCannotBeTrackedIsLoggedAndTheFeedKeepsWorking()
    {
        _service.RaiseToolExecution(new ToolExecutionEvent
        {
            Phase = ToolExecutionPhase.Start,
            ToolCallId = "t1",
            Name = "write",
            Args = new JObject { ["path"] = "a.txt", ["content"] = "x" },
        });
        await _feed.WhenIdleAsync();
        Assert.Contains("Change tracking failed", _logger.Errors);
    }
}
