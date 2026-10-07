using Newtonsoft.Json.Linq;
using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

public class PromptTrackerTests
{
    private readonly List<SessionPhase> _phases = new();
    private readonly PromptTracker _t;

    public PromptTrackerTests()
    {
        _t = new PromptTracker(phase => _phases.Add(phase));
    }

    private static JObject Result(string id, string status = "completed", bool sessionSettled = true, bool agentInvoked = true, string? error = null)
    {
        var frame = new JObject { ["type"] = "prompt_result", ["id"] = id, ["agentInvoked"] = agentInvoked, ["status"] = status, ["sessionSettled"] = sessionSettled };
        if (error != null) frame["error"] = new JObject { ["message"] = error, ["retryable"] = true };
        return frame;
    }

    private static string Describe(PromptOutcome o) => $"{o.Status}:{o.Error}:{o.SessionSettled}";

    [Fact]
    public async Task TreatsThePromptAckAsAdmissionNotCompletion()
    {
        var outcome = _t.SubmitAsync("p1");
        Assert.Equal(SessionPhase.Submitting, _t.Phase);
        _t.Acknowledged("p1", null);
        Assert.Equal(SessionPhase.Running, _t.Phase);
        _t.AgentStart();
        _t.AgentEnd(true);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.False(outcome.IsCompleted);
        _t.PromptResult(Result("p1"));
        Assert.Equal("Completed::True", Describe(await outcome));
        Assert.Equal(SessionPhase.Idle, _t.Phase);
        Assert.Equal(new[] { SessionPhase.Submitting, SessionPhase.Running, SessionPhase.Idle }, _phases);
    }

    [Fact]
    public async Task GoesYieldedOnPromptResultWithSessionSettledFalseAndIdleOnSessionSettled()
    {
        var outcome = _t.SubmitAsync("p1");
        _t.Acknowledged("p1", true);
        _t.AgentStart();
        _t.AgentEnd(true);
        _t.PromptResult(Result("p1", sessionSettled: false));
        Assert.Equal("Completed::False", Describe(await outcome));
        Assert.Equal(SessionPhase.Yielded, _t.Phase);
        _t.AgentStart();
        Assert.Equal(SessionPhase.Running, _t.Phase);
        _t.AgentEnd(true);
        Assert.Equal(SessionPhase.Yielded, _t.Phase);
        _t.SessionSettled();
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }

    [Fact]
    public void KeepsRunningThroughANonYieldedAgentEnd()
    {
        _t.SubmitAsync("p1");
        _t.Acknowledged("p1", true);
        _t.AgentStart();
        _t.AgentEnd(false);
        Assert.Equal(SessionPhase.Running, _t.Phase);
    }

    [Fact]
    public async Task CompletesLocallyWhenTheAckSaysAgentInvokedFalse()
    {
        var outcome = _t.SubmitAsync("p1");
        _t.Acknowledged("p1", false);
        Assert.Equal("Local::True", Describe(await outcome));
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }

    [Fact]
    public async Task ResolvesErrorWhenThePromptCommandFails()
    {
        var outcome = _t.SubmitAsync("p1");
        _t.Rejected("p1", "No model selected");
        Assert.Equal("Error:No model selected:True", Describe(await outcome));
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }

    [Fact]
    public async Task EntersAbortingOnAbortAndResolvesThePromptAsAborted()
    {
        var outcome = _t.SubmitAsync("p1");
        _t.Acknowledged("p1", true);
        _t.AgentStart();
        _t.AbortRequested();
        Assert.Equal(SessionPhase.Aborting, _t.Phase);
        _t.AgentStart();
        Assert.Equal(SessionPhase.Aborting, _t.Phase);
        _t.AgentEnd(true);
        _t.PromptResult(Result("p1", "aborted"));
        Assert.Equal("Aborted::True", Describe(await outcome));
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }

    [Fact]
    public void IgnoresAbortWhileIdle()
    {
        _t.AbortRequested();
        Assert.Equal(SessionPhase.Idle, _t.Phase);
        Assert.Empty(_phases);
    }

    [Fact]
    public void DoesNotCarryAnAbortWhileYieldedIntoTheNextPrompt()
    {
        _t.SubmitAsync("p1");
        _t.Acknowledged("p1", true);
        _t.AgentStart();
        _t.AgentEnd(true);
        _t.PromptResult(Result("p1", sessionSettled: false));
        Assert.Equal(SessionPhase.Yielded, _t.Phase);
        _t.AbortRequested();
        Assert.Equal(SessionPhase.Yielded, _t.Phase);
        _t.SubmitAsync("p2");
        Assert.Equal(SessionPhase.Submitting, _t.Phase);
        _t.Acknowledged("p2", true);
        _t.AgentStart();
        Assert.Equal(SessionPhase.Running, _t.Phase);
    }

    [Fact]
    public async Task CarriesThePromptResultErrorMessage()
    {
        var outcome = _t.SubmitAsync("p1");
        _t.Acknowledged("p1", true);
        _t.PromptResult(Result("p1", "error", error: "rate limited"));
        Assert.Equal("Error:rate limited:True", Describe(await outcome));
    }

    [Fact]
    public async Task OnlyCompletesThePromptMatchingThePromptResultId()
    {
        var first = _t.SubmitAsync("p1");
        var second = _t.SubmitAsync("p2");
        _t.Acknowledged("p1", true);
        _t.Acknowledged("p2", true);
        _t.PromptResult(Result("p2", sessionSettled: false));
        Assert.Equal(PromptStatus.Completed, (await second).Status);
        Assert.False(first.IsCompleted);
        Assert.Equal(SessionPhase.Running, _t.Phase);
    }

    [Fact]
    public async Task ResolvesEveryPendingPromptWithErrorWhenTheProcessTerminates()
    {
        var a = _t.SubmitAsync("p1");
        var b = _t.SubmitAsync("p2");
        _t.Acknowledged("p1", true);
        _t.Terminate("OMP process exited (code 3)");
        Assert.Equal("Error:OMP process exited (code 3):True", Describe(await a));
        Assert.Equal(PromptStatus.Error, (await b).Status);
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }

    [Fact]
    public void TracksAgentRunsStartedWithoutAPrompt()
    {
        _t.AgentStart();
        Assert.Equal(SessionPhase.Running, _t.Phase);
        _t.AgentEnd(true);
        Assert.Equal(SessionPhase.Yielded, _t.Phase);
        _t.SessionSettled();
        Assert.Equal(SessionPhase.Idle, _t.Phase);
    }
}
