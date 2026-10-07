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
    private sealed class Logger : IOmpLogger
    {
        public readonly ConcurrentQueue<string> Errors = new();

        public void Error(string message, Exception? error = null) => Errors.Enqueue(message);

        public void Warn(string message, Exception? error = null) { }

        public void Info(string message, Exception? error = null) { }

        public void Debug(string message, Exception? error = null) { }

        public bool TraceEnabled => false;

        public void Trace(string direction, string frame) { }
    }

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
