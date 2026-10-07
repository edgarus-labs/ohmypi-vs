using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Omp.Core.Processes;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Service;

/// <summary>The real <see cref="OmpService"/> and <see cref="OmpProcess"/> driving the fake OMP fixture with Node.</summary>
public sealed class OmpServiceFakeOmpTests : IAsyncLifetime
{
    private readonly string _dir = TempDirectory.Create("omp-service-");
    private readonly List<Harness> _harnesses = new();
    private readonly NodeMemoryGuard _memory;
    private string _launcher = "";
    private string LogFile => Path.Combine(_dir, "commands.log");

    public OmpServiceFakeOmpTests()
    {
        _memory = new NodeMemoryGuard(() => { lock (_harnesses) return _harnesses.Select(h => h.Logger).ToArray(); });
    }

    public ValueTask InitializeAsync()
    {
        _launcher = FakeOmp.WriteLauncher(_dir, "fake-omp", FakeOmp.Script);
        return default;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var harness in _harnesses)
        {
            await Wait.Settle(harness.Service.StopAsync());
            FakeOmp.KillLeftovers(harness.Logger);
        }
        foreach (var harness in _harnesses)
        {
            foreach (var pid in FakeOmp.ReportedPids(harness.Logger))
                await Wait.For(() => !FakeOmp.IsAlive(pid), 5000, $"node {pid} gone");
        }
        await _memory.DisposeAsync();
        await TempDirectory.DeleteAsync(_dir);
    }

    private sealed class Harness
    {
        public required OmpService Service { get; init; }
        public required MemoryLogger Logger { get; init; }
        public List<SessionPhase> Phases { get; } = new();
        public List<ConnectionState> States { get; } = new();
        public List<TranscriptItem> Items { get; } = new();
        public List<ToolExecutionEvent> Tools { get; } = new();
        public List<InteractionRequest> Interactions { get; } = new();
        public List<string> Cancelled { get; } = new();
        public List<PresentationRequest> Presentations { get; } = new();

        public IReadOnlyList<string> AssistantTexts() => Service.Transcript.OfType<AssistantItem>().Select(i => i.Text).ToArray();

        public IReadOnlyList<string> Notices() => Service.Transcript.OfType<NoticeItem>().Select(n => $"{n.Level.ToString().ToLowerInvariant()}: {n.Text}").ToArray();

        public T Locked<T>(Func<T> read)
        {
            lock (this) return read();
        }
    }

    private Harness Create(bool autoRestart = false, bool trace = false, IDictionary<string, string>? env = null, Action<OmpServiceTuning>? tune = null, string? executable = null, IHostTools? hostTools = null)
    {
        var logger = new MemoryLogger(trace);
        var tuning = new OmpServiceTuning { RestartDelaysMs = new[] { 20, 20, 20 }, RestartWindowMs = 60_000, ShutdownGraceMs = 1000 };
        tune?.Invoke(tuning);
        var service = new OmpService(
            new OmpServiceOptions
            {
                Executable = executable ?? _launcher,
                Cwd = _dir,
                Environment = FakeOmp.Environment(Path.Combine(_dir, "sessions"), LogFile, env),
                Logger = logger,
                AutoRestart = autoRestart,
                HostTools = hostTools,
            },
            tuning);
        var h = new Harness { Service = service, Logger = logger };
        service.SessionChanged += (_, s) => { lock (h) if (h.Phases.Count == 0 || h.Phases[h.Phases.Count - 1] != s.Phase) h.Phases.Add(s.Phase); };
        service.ConnectionChanged += (_, c) => { lock (h) h.States.Add(c.State); };
        service.TranscriptItemChanged += (_, i) => { lock (h) h.Items.Add(i); };
        service.ToolExecution += (_, t) => { lock (h) h.Tools.Add(t); };
        service.InteractionRequested += (_, r) => { lock (h) h.Interactions.Add(r); };
        service.InteractionCancelled += (_, id) => { lock (h) h.Cancelled.Add(id); };
        service.Presentation += (_, p) => { lock (h) h.Presentations.Add(p); };
        lock (_harnesses) _harnesses.Add(h);
        return h;
    }

    private IReadOnlyList<JObject> SentCommands()
    {
        if (!File.Exists(LogFile)) return Array.Empty<JObject>();
        using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
    }

    private JObject? Command(string type) => SentCommands().FirstOrDefault(c => (string?)c["type"] == type);

    private static async Task<int> NodePid(Harness h)
    {
        await Wait.For(() => FakeOmp.ReportedPids(h.Logger).Count > 0, 5000, "node pid");
        return FakeOmp.ReportedPids(h.Logger).Last();
    }

    [Fact]
    public async Task StartsConfiguresSubscriptionsStreamsAPromptToSettlementAndStopsWithoutOrphans()
    {
        var h = Create();
        await h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
        Assert.Equal(2, h.Service.Connection.ProtocolVersion);
        var pid = h.Service.Connection.Pid!.Value;
        var node = await NodePid(h);
        Assert.True(FakeOmp.IsAlive(pid));
        Assert.EndsWith(".jsonl", h.Service.Session.SessionFile);
        Assert.Equal("fake-large", h.Service.Session.Model!.Id);
        Assert.Equal(new[] { "off", "auto", "low", "medium", "high" }, h.Service.Session.AvailableThinkingLevels);
        var todo = Assert.Single(h.Service.Session.Todos);
        Assert.Equal(("Todos", "Fake task", "in_progress"), (todo.Name, todo.Tasks[0].Content, todo.Tasks[0].Status));
        Assert.Equal((1200L, 0.6), (h.Service.Session.ContextUsage!.Tokens, h.Service.Session.ContextUsage.Percent));
        Assert.Equal(new[] { "main" }, h.Service.Agents.Select(a => a.Id));

        Assert.Equal("progress", (string?)Command("set_subagent_subscription")!["level"]);
        Assert.True((bool)Command("set_ask_dialog")!["enabled"]!);
        Assert.Equal("delta", (string?)Command("set_event_filter")!["messageUpdates"]);
        Assert.NotNull(Command("new_session"));

        var outcome = await h.Service.PromptAsync("hello there");
        Assert.Equal((PromptStatus.Completed, true), (outcome.Status, outcome.SessionSettled));
        Assert.Equal(SessionPhase.Idle, h.Service.Session.Phase);
        Assert.Equal(new[] { SessionPhase.Submitting, SessionPhase.Running, SessionPhase.Idle }, h.Locked(() => h.Phases.Skip(h.Phases.Count - 3).ToArray()));
        Assert.Contains(h.Locked(() => h.Items.ToArray()), i => i is AssistantItem { Streaming: true });
        Assert.Equal(new[] { typeof(UserItem), typeof(AssistantItem), typeof(TurnSummaryItem) }, h.Service.Transcript.Select(i => i.GetType()));
        Assert.True(((TurnSummaryItem)h.Service.Transcript[2]).OutputTokens > 0);
        Assert.Equal(new[] { "Echo: hello there" }, h.AssistantTexts());
        Assert.True(h.Service.Session.CostUsd > 0);

        await h.Service.StopAsync();
        Assert.Equal(ConnectionState.Stopped, h.Service.Connection.State);
        Assert.Contains(ConnectionState.Starting, h.States);
        Assert.Contains(ConnectionState.Ready, h.States);
        await Wait.For(() => !FakeOmp.IsAlive(pid) && !FakeOmp.IsAlive(node), 3000, "omp process tree gone");
    }

    [Fact]
    public async Task RoundTripsNonAsciiPromptsAndReplies()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("zażółć gęślą jaźń 😀");
        Assert.Equal(new[] { "Echo: zażółć gęślą jaźń 😀" }, h.AssistantTexts());
        Assert.Equal("zażółć gęślą jaźń 😀", h.Service.Transcript.OfType<UserItem>().Single().Text);
    }

    [Fact]
    public async Task MapsModelsAndAppliesModelThinkingLevelFastModeAndSessionName()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var models = await h.Service.ListModelsAsync();
        Assert.Equal(new[] { "fake-anthropic/fake-large/low,medium,high", "fake-openai/fake-small/" }, models.Select(m => $"{m.Provider}/{m.Id}/{string.Join(",", m.ThinkingEfforts)}"));
        await h.Service.SetModelAsync("fake-openai", "fake-small");
        Assert.Equal("fake-small", h.Service.Session.Model!.Id);
        Assert.Equal(new[] { "off" }, h.Service.Session.AvailableThinkingLevels);
        await Wait.For(() => h.Service.Agents[0].Model == "fake-openai/fake-small", 3000, "main agent model");
        Assert.Contains("Model not found", (await Assert.ThrowsAsync<OmpRequestException>(() => h.Service.SetModelAsync("nope", "x"))).Message);
        await h.Service.SetThinkingLevelAsync("high");
        Assert.Equal("high", h.Service.Session.ThinkingLevel);
        await h.Service.SetFastModeAsync(true);
        Assert.True(h.Service.Session.FastModeEnabled);
        Assert.True(h.Service.Session.FastModeActive);
        await h.Service.SetSessionNameAsync("Renamed");
        Assert.Equal("Renamed", h.Service.Session.SessionName);
    }

    [Fact]
    public async Task ReportsToolExecutionsWithEditDetailsAndChangesTheFile()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var target = Path.Combine(_dir, "target.ts");
        File.WriteAllText(target, "const a = 1;\n");
        var outcome = await h.Service.PromptAsync($"please tool {target}");
        Assert.Equal(PromptStatus.Completed, outcome.Status);
        Assert.Contains("edited by fake-omp", File.ReadAllText(target));
        var tools = h.Locked(() => h.Tools.ToArray());
        Assert.Equal(new[] { "Start:read", "End:read", "Start:edit", "End:edit" }, tools.Select(t => $"{t.Phase}:{t.Name}"));
        var details = tools.Single(t => t.Phase == ToolExecutionPhase.End && t.Name == "edit").Result!.Details!;
        Assert.Equal(target, (string?)details["path"]);
        Assert.Equal("const a = 1;\n", (string?)details["oldText"]);
        Assert.Contains("edited by fake-omp", (string?)details["newText"]);
        Assert.Equal(new[] { ToolStatus.Done, ToolStatus.Done }, h.Service.Transcript.OfType<ToolItem>().Where(t => t.Id.StartsWith("call-")).Select(t => t.Status));
    }

    [Fact]
    public async Task TracksTheChangeAToolMadeWithTheChangeModel()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var target = Path.Combine(_dir, "tracked.ts");
        File.WriteAllText(target, "const a = 1;\n");
        var changes = new Omp.Core.Changes.ChangeModel(path => Omp.Core.Changes.ChangeModel.ReadSnapshotAsync(path, h.Logger), h.Logger);
        var scope = new Omp.Core.Changes.ChangeScope { Cwd = _dir, Roots = new[] { _dir } };
        var applied = new List<Task>();
        h.Service.ToolExecution += (_, e) => { lock (applied) applied.Add(changes.ApplyAsync(e, scope)); };
        await h.Service.PromptAsync($"please tool {target}");
        Task[] pending;
        lock (applied) pending = applied.ToArray();
        await Task.WhenAll(pending);
        var change = Assert.Single(changes.Changes);
        Assert.Equal((target, Omp.Core.Changes.ChangeStatus.Modified, 1, 0), (change.Path, change.Status, change.Added, change.Removed));
        Assert.Equal("const a = 1;\n", changes.Baseline(target)!.Content);
    }

    [Fact]
    public async Task RoundTripsAConfirmInteraction()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("approve this");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "confirm request");
        var request = Assert.IsType<ConfirmRequest>(h.Interactions[0]);
        Assert.Equal("Approve fake action?", request.Title);
        Assert.Equal(60000, request.TimeoutMs);
        h.Service.RespondInteraction(request.Id, InteractionResponse.FromConfirmed(true));
        Assert.Equal(PromptStatus.Completed, (await outcome).Status);
        Assert.Equal(new[] { "Decision: Approved" }, h.AssistantTexts());
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "extension_ui_response" && (string?)c["id"] == request.Id && (bool?)c["confirmed"] == true);
    }

    [Fact]
    public async Task RoundTripsAnAskInteractionWithAnswers()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("ask me");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "ask request");
        var request = Assert.IsType<AskRequest>(h.Interactions[0]);
        Assert.Equal(
            new[] { "db:Postgres,SQLite:False:0", "features:Auth,Search:True:" },
            request.Questions.Select(q => $"{q.Id}:{string.Join(",", q.Options.Select(o => o.Label))}:{q.Multi}:{q.Recommended}"));
        Assert.Equal("Embedded", request.Questions[0].Options[1].Description);
        h.Service.RespondInteraction(request.Id, InteractionResponse.FromAnswers(new[]
        {
            new AskAnswer { Id = "db", CustomInput = "DuckDB" },
            new AskAnswer { Id = "features", SelectedOptions = new[] { "Auth" } },
        }));
        await outcome;
        Assert.Contains("DuckDB", h.AssistantTexts()[0]);
        Assert.Contains("Auth", h.AssistantTexts()[0]);
    }

    [Fact]
    public async Task NeverTracesInputAnswersAndMarksAnUnflaggedInputAsNotSecret()
    {
        var h = Create(trace: true);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("input please");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "input request");
        var request = Assert.IsType<InputRequest>(h.Interactions[0]);
        Assert.False(request.Secret);
        Assert.Equal(("Token", "paste here"), (request.Title, request.Placeholder));
        h.Service.RespondInteraction(request.Id, InteractionResponse.FromValue("hunter2-token"));
        await outcome;
        Assert.Equal(new[] { "Received 13 characters" }, h.AssistantTexts());
        Assert.Contains("extension_ui_response", h.Logger.Text("trace"));
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "extension_ui_response" && (string?)c["value"] == "hunter2-token");
        Assert.DoesNotContain(h.Logger.Records, r => r.Message.Contains("hunter2-token"));
    }

    [Fact]
    public async Task RoundTripsAnEditorInteraction()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("editor please");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "editor request");
        var request = Assert.IsType<EditorRequest>(h.Interactions[0]);
        Assert.Equal(("Edit the plan", "step 1", (int?)null), (request.Title, request.Prefill, request.TimeoutMs));
        h.Service.RespondInteraction(request.Id, InteractionResponse.FromValue("step 2"));
        await outcome;
        Assert.Equal(new[] { "Edited: step 2" }, h.AssistantTexts());
    }

    [Fact]
    public async Task FallsBackToASelectInteractionWhenOmpHasNoAskDialog()
    {
        var h = Create(env: new Dictionary<string, string> { ["FAKE_OMP_NO_ASK_DIALOG"] = "1" });
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("ask dialog unavailable", h.Logger.Text("info"));
        var outcome = h.Service.PromptAsync("ask me");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "select request");
        var request = Assert.IsType<SelectRequest>(h.Interactions[0]);
        Assert.Equal(("Which database?", 60000), (request.Title, request.TimeoutMs!.Value));
        Assert.Equal(new[] { "Postgres:", "SQLite:Embedded" }, request.Options.Select(o => $"{o.Label}:{o.Description}"));
        h.Service.RespondInteraction(request.Id, InteractionResponse.FromValue("SQLite"));
        await outcome;
        Assert.Equal(new[] { "Selected: SQLite" }, h.AssistantTexts());
    }

    [Fact]
    public async Task ForwardsPresentationRequestsNoticesSessionTitlesAndStreamedToolOutput()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("present things");
        Assert.Equal(new[]
        {
            "notify Warning Heads up", "status fake busy", "status fake ", "url https://fake.invalid/launch Sign in", "editor draft text",
        }, h.Locked(() => h.Presentations.ToArray()).Select(p => p switch
        {
            NotifyPresentation n => $"notify {n.Level} {n.Message}",
            StatusPresentation s => $"status {s.Key} {s.Text}",
            OpenUrlPresentation u => $"url {u.Url} {u.Instructions}",
            EditorTextPresentation e => $"editor {e.Text}",
            _ => "?",
        }));
        Assert.Equal(new[]
        {
            "info: Fake notice",
            "warning: Compaction failed: too small",
            "warning: Retrying (attempt 1/3) in 2s: overloaded",
            "error: Retry failed: still overloaded",
        }, h.Notices());
        Assert.Equal("Presented", h.Service.Session.SessionName);
        Assert.False(h.Service.Session.IsCompacting);
        Assert.Contains(h.Locked(() => h.Items.ToArray()), i => i is ToolItem { Partial: "streamed line" });
        await Wait.For(() => SentCommands().Count(c => (string?)c["type"] == "get_state") >= 2, 3000, "state refresh after config_update");
    }

    [Fact]
    public async Task AbortsARunningPromptWhileASteerIsQueued()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("go slow");
        await Wait.For(() => h.Service.Transcript.OfType<AssistantItem>().Any(i => i.Text.Contains("tick")), 10000, "streaming");
        Assert.Equal(SessionPhase.Running, h.Service.Session.Phase);
        var steer = h.Service.PromptAsync("and more");
        await Wait.For(() => SentCommands().Any(c => (string?)c["type"] == "prompt" && (string?)c["message"] == "and more"), 3000, "steer sent");
        Assert.Equal("steer", (string?)SentCommands().First(c => (string?)c["message"] == "and more")["streamingBehavior"]);
        await Wait.For(() => h.Service.Session.Queue.Steering.Contains("and more"), 3000, "queued steer");
        await h.Service.AbortAsync();
        var result = await outcome;
        Assert.Equal((PromptStatus.Aborted, false), (result.Status, result.SessionSettled));
        Assert.Equal(PromptStatus.Completed, (await steer).Status);
        await Wait.For(() => h.Service.Session.Phase == SessionPhase.Idle, 3000, "idle");
        Assert.Contains(SessionPhase.Aborting, h.Locked(() => h.Phases.ToArray()));
    }

    /// <summary>Host tools that record their calls and answer through <see cref="Answer"/>.</summary>
    private sealed class RecordingHostTools : IHostTools
    {
        public List<(string Name, JObject Arguments)> Calls { get; } = new();
        public Func<string, JObject, CancellationToken, Task<HostToolResult>> Answer { get; set; } = (_, _, _) => Task.FromResult(HostToolResult.Text("done"));

        public IReadOnlyList<HostToolDefinition> Definitions { get; } = new[]
        {
            new HostToolDefinition("vs_build", "Builds the solution.", JObject.Parse("{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\"}}}")),
        };

        public Task<HostToolResult> InvokeAsync(string name, JObject arguments, CancellationToken cancellationToken)
        {
            lock (Calls) Calls.Add((name, arguments));
            return Answer(name, arguments, cancellationToken);
        }
    }

    [Fact]
    public async Task RegistersHostToolsAtStartAndAnswersTheirCalls()
    {
        var tools = new RecordingHostTools { Answer = (_, _, _) => Task.FromResult(HostToolResult.Text("built 3 projects")) };
        var h = Create(hostTools: tools);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var registered = Command("set_host_tools")!;
        Assert.Equal("vs_build", (string?)registered["tools"]![0]!["name"]);
        Assert.Equal("Builds the solution.", (string?)registered["tools"]![0]!["description"]);
        Assert.Equal("object", (string?)registered["tools"]![0]!["parameters"]!["type"]);

        await h.Service.PromptAsync("please hosttool vs_build");
        Assert.Contains("Host said: built 3 projects", h.AssistantTexts());
        var call = Assert.Single(tools.Calls);
        Assert.Equal(("vs_build", "solution"), (call.Name, (string?)call.Arguments["target"]));
    }

    [Fact]
    public async Task AFailingHostToolAnswersAsAToolError()
    {
        var tools = new RecordingHostTools { Answer = (_, _, _) => throw new InvalidOperationException("no solution is open") };
        var h = Create(hostTools: tools);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("please hosttool vs_build");
        Assert.Contains("Host error: no solution is open", h.AssistantTexts());
    }

    [Fact]
    public async Task OmpCancellingAHostToolCancelsItsWork()
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new RecordingHostTools
        {
            Answer = async (_, _, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
                return HostToolResult.Text("never");
            },
        };
        var h = Create(hostTools: tools);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("please hostcancel vs_build");
        Assert.Same(cancelled.Task, await Task.WhenAny(cancelled.Task, Task.Delay(5000, TestContext.Current.CancellationToken)));
        Assert.Contains(h.AssistantTexts(), text => text.StartsWith("Host error: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AbortCancelsEveryRunningSubagentSoAFinishingSessionSettles()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await h.Service.PromptAsync("delegate backgroundagent work");
        Assert.False(result.SessionSettled);
        Assert.Equal(SessionPhase.Yielded, h.Service.Session.Phase);
        await Wait.For(() => h.Service.Agents.Any(a => a.Id == "Gamma" && a.Status == AgentStatus.Running), 3000, "background subagent");
        await h.Service.AbortAsync();
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "cancel_subagent" && (string?)c["subagentId"] == "Gamma");
        await Wait.For(() => h.Service.Session.Phase == SessionPhase.Idle, 3000, "settled after stop");
    }

    [Fact]
    public async Task SummarizesATurnStoppedWhileFinishingAsAbortedOnceItSettles()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("delegate backgroundagent work");
        await Wait.For(() => h.Service.Agents.Any(a => a.Id == "Gamma" && a.Status == AgentStatus.Running), 3000, "background subagent");
        Assert.Empty(h.Service.Transcript.OfType<TurnSummaryItem>());
        await h.Service.AbortAsync();
        await Wait.For(() => h.Service.Session.Phase == SessionPhase.Idle, 3000, "settled after stop");
        var summary = Assert.IsType<TurnSummaryItem>(h.Service.Transcript.Last());
        Assert.True(summary.Aborted);
        Assert.True(summary.OutputTokens > 0);
    }

    [Fact]
    public async Task SendsPastedImagesWithThePromptAsOmpImageContent()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = await h.Service.PromptAsync("what is in this picture", PromptMode.Auto, new[] { new PromptImage { Data = "iVBORw0KGgo=", MimeType = "image/png" } });
        Assert.Equal(PromptStatus.Completed, outcome.Status);
        var sent = SentCommands().First(c => (string?)c["type"] == "prompt" && (string?)c["message"] == "what is in this picture");
        Assert.Equal("[{\"type\":\"image\",\"data\":\"iVBORw0KGgo=\",\"mimeType\":\"image/png\"}]", sent["images"]!.ToString(Newtonsoft.Json.Formatting.None));
    }

    [Fact]
    public async Task ReadsOmpsAutoEffortSelectorFromTheSessionJournalAndOffersAuto()
    {
        var h = Create(env: new Dictionary<string, string> { ["FAKE_OMP_THINKING"] = "auto" });
        await h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        Assert.Equal(("auto", "high"), (h.Service.Session.ThinkingSelector, h.Service.Session.ThinkingLevel));
        Assert.Equal(new[] { "off", "auto", "low", "medium", "high" }, h.Service.Session.AvailableThinkingLevels);
    }

    [Fact]
    public async Task SelectsAutoKeepsItWhileOmpResolvesEachTurnAndLeavesItForAnExplicitLevel()
    {
        var h = Create();
        await h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        await h.Service.SetThinkingLevelAsync("auto");
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "set_thinking_level" && (string?)c["level"] == "auto");
        Assert.Equal("auto", h.Service.Session.ThinkingSelector);
        await h.Service.PromptAsync("hello");
        Assert.Equal(("auto", "low"), (h.Service.Session.ThinkingSelector, h.Service.Session.ThinkingResolved));
        await h.Service.SetThinkingLevelAsync("medium");
        Assert.Equal(("medium", (string?)null), (h.Service.Session.ThinkingSelector, h.Service.Session.ThinkingResolved));
    }

    [Fact]
    public async Task WithdrawsAPendingInteractionWhenOmpCancelsItOnAbort()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = h.Service.PromptAsync("approve this");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "confirm request");
        await h.Service.AbortAsync();
        Assert.Equal(PromptStatus.Aborted, (await outcome).Status);
        Assert.Equal(new[] { h.Interactions[0].Id }, h.Locked(() => h.Cancelled.ToArray()));
    }

    [Fact]
    public async Task GoesYieldedOnAnUnsettledPromptResultAndIdleOnSessionSettled()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = await h.Service.PromptAsync("run in background");
        Assert.Equal((PromptStatus.Completed, false), (outcome.Status, outcome.SessionSettled));
        Assert.Equal(SessionPhase.Yielded, h.Service.Session.Phase);
        await Wait.For(() => h.Service.Session.Phase == SessionPhase.Idle, 10000, "settled");
        Assert.Equal(new[] { SessionPhase.Yielded, SessionPhase.Running, SessionPhase.Yielded, SessionPhase.Idle }, h.Locked(() => h.Phases.Skip(h.Phases.Count - 4).ToArray()));
        Assert.Equal(new[] { "Started a background job.", "Background job finished." }, h.AssistantTexts());
        var summary = Assert.IsType<TurnSummaryItem>(h.Service.Transcript.Last());
        Assert.Single(h.Service.Transcript.OfType<TurnSummaryItem>());
        Assert.Equal(h.Service.Transcript.OfType<AssistantItem>().Sum(a => a.Usage!.Output), summary.OutputTokens);
    }

    [Fact]
    public async Task TracksSubagentsAndLoadsTheirTranscripts()
    {
        var h = Create();
        var agentEvents = 0;
        h.Service.AgentsChanged += (_, _) => Interlocked.Increment(ref agentEvents);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("spawn subagent work");
        var subs = h.Service.Agents.Where(a => a.Id != "main").ToArray();
        Assert.Equal(new[] { "Alpha:Completed:main:2", "Beta:Completed:main:2" }, subs.Select(a => $"{a.Id}:{a.Status}:{a.ParentId}:{a.ToolCount}"));
        Assert.True(agentEvents > 4);
        var transcript = await h.Service.GetAgentTranscriptAsync("Alpha");
        Assert.Equal(new[] { typeof(UserItem), typeof(AssistantItem) }, transcript.Select(i => i.GetType()));
        Assert.Equal("Alpha reporting", ((AssistantItem)transcript[1]).Text);
        Assert.False(await h.Service.CancelAgentAsync("Alpha"));
        Assert.Equal(h.Service.Transcript.Count, (await h.Service.GetAgentTranscriptAsync("main")).Count);
        await Assert.ThrowsAsync<OmpRequestException>(() => h.Service.SteerAgentAsync("Alpha", "faster"));
    }

    [Fact]
    public async Task ResumesASessionFileShowsItsHistoryListsSessionsAndSwitches()
    {
        var first = Create();
        await first.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        await first.Service.PromptAsync("remember me");
        var file = first.Service.Session.SessionFile!;
        await first.Service.StopAsync();

        var second = Create();
        await second.Service.StartAsync(new StartOptions { ResumeSessionFile = file }, TestContext.Current.CancellationToken);
        Assert.Equal(file, second.Service.Session.SessionFile);
        Assert.Equal(new[] { "user:remember me", "assistant:Echo: remember me" }, second.Service.Transcript.Select(i => i switch
        {
            UserItem u => $"user:{u.Text}",
            AssistantItem a => $"assistant:{a.Text}",
            _ => i.GetType().Name,
        }));
        var sessions = await second.Service.ListSessionsAsync();
        Assert.Contains(sessions, s => s.Path == file && s.FirstMessage == "remember me");

        IReadOnlyList<TranscriptItem>? reset = null;
        second.Service.TranscriptReset += (_, items) => reset = items;
        await second.Service.NewSessionAsync();
        Assert.NotEqual(file, second.Service.Session.SessionFile);
        Assert.Empty(second.Service.Transcript);
        Assert.Empty(reset!);
        await second.Service.SwitchSessionAsync(file);
        Assert.Equal(2, second.Service.Transcript.Count);
        Assert.Equal(file, second.Service.Session.SessionFile);
    }

    [Fact]
    public async Task RestartRebindsTheCurrentSessionFile()
    {
        var h = Create();
        await h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("keep me");
        var file = h.Service.Session.SessionFile!;
        var pid = h.Service.Connection.Pid;
        await h.Service.RestartAsync();
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
        Assert.NotEqual(pid, h.Service.Connection.Pid);
        Assert.Equal(file, h.Service.Session.SessionFile);
        Assert.Equal(2, h.Service.Transcript.Count);
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "switch_session" && (string?)c["sessionPath"] == file);
    }

    [Fact]
    public async Task AutoRestartsAfterACrashRebindingTheSessionThenGivesUpAfterTheBudget()
    {
        var h = Create(autoRestart: true);
        await h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        var file = h.Service.Session.SessionFile!;
        var firstPid = h.Service.Connection.Pid!.Value;
        var outcome = await h.Service.PromptAsync("please crash");
        Assert.Equal(PromptStatus.Error, outcome.Status);
        Assert.Contains("exited", outcome.Error);
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Ready && h.Service.Connection.Pid != firstPid, 10000, "restarted");
        Assert.Contains(ConnectionState.Restarting, h.Locked(() => h.States.ToArray()));
        Assert.False(FakeOmp.IsAlive(firstPid));
        Assert.Equal(file, h.Service.Session.SessionFile);
        Assert.Contains(SentCommands(), c => (string?)c["type"] == "switch_session" && (string?)c["sessionPath"] == file);
        Assert.Equal(SessionPhase.Idle, h.Service.Session.Phase);
        Assert.Matches(@"(?m)^error: OMP process .*exited \(code 3\).*restarted", string.Join("\n", h.Notices()));

        for (var i = 0; i < 2; i++)
        {
            var pid = h.Service.Connection.Pid;
            await h.Service.PromptAsync("crash again");
            await Wait.For(() => h.Service.Connection.State == ConnectionState.Ready && h.Service.Connection.Pid != pid, 10000, $"restart {i + 2}");
        }
        await h.Service.PromptAsync("crash once more");
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Failed, 10000, "failed");
        Assert.Contains("gave up after 3 restarts", h.Service.Connection.Detail);
        Assert.Contains("gave up after 3 restarts", h.Logger.Text("error"));
        Assert.Matches("^error: .*gave up after 3 restarts", h.Notices().Last());
        Assert.Contains("not running", (await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.PromptAsync("hello"))).Message);
    }

    [Fact]
    public async Task ReportsAnUnexpectedExitWithoutAutoRestartInTheTranscript()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("please crash");
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Failed, 10000, "failed");
        Assert.Contains("exited (code 3)", h.Service.Connection.Detail);
        Assert.Matches(@"^error: OMP process .*exited \(code 3\) unexpectedly", h.Notices().Last());
        Assert.Matches(@"exited \(code 3\) unexpectedly", h.Logger.Text("warn"));
    }

    [Fact]
    public async Task RestoresTheRestartBudgetOnceTheWindowHasPassed()
    {
        var h = Create(autoRestart: true, tune: t => { t.RestartDelaysMs = new[] { 20 }; t.RestartWindowMs = 300; });
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
        {
            var pid = h.Service.Connection.Pid;
            await h.Service.PromptAsync("please crash");
            await Wait.For(() => h.Service.Connection.State == ConnectionState.Ready && h.Service.Connection.Pid != pid, 10000, $"restart {i + 1}");
            await Task.Delay(400, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DoesNotRelaunchAfterStopWhileARestartIsPending()
    {
        var h = Create(autoRestart: true, tune: t => t.RestartDelaysMs = new[] { 300 });
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("please crash");
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Restarting, 10000, "restarting");
        await h.Service.StopAsync();
        var after = h.Locked(() => h.States.Count);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Stopped, h.Service.Connection.State);
        Assert.Equal(after, h.Locked(() => h.States.Count));
    }

    [Fact]
    public async Task ReschedulesWhenARestartAttemptFailsToLaunch()
    {
        var marker = Path.Combine(_dir, "refuse-start");
        var h = Create(autoRestart: true, env: new Dictionary<string, string> { ["FAKE_OMP_EXIT_IF_EXISTS"] = marker }, tune: t => t.RestartDelaysMs = new[] { 20, 1500, 20 });
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        File.WriteAllText(marker, "");
        await h.Service.PromptAsync("please crash");
        await Wait.For(() => (h.Service.Connection.Detail ?? "").Contains("restart failed"), 10000, "failed restart rescheduled");
        Assert.Equal(ConnectionState.Restarting, h.Service.Connection.State);
        File.Delete(marker);
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Ready, 10000, "ready after retry");
        Assert.True(h.Locked(() => h.States.Count(s => s == ConnectionState.Restarting)) >= 2);
    }

    [Fact]
    public async Task WithdrawsPendingInteractionsWhenOmpExits()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var node = await NodePid(h);
        _ = h.Service.PromptAsync("approve this");
        await Wait.For(() => h.Locked(() => h.Interactions.Count) > 0, 10000, "confirm request");
        using (var process = Process.GetProcessById(node)) process.Kill();
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Failed, 10000, "failed");
        Assert.Equal(new[] { h.Interactions[0].Id }, h.Locked(() => h.Cancelled.ToArray()));
    }

    [Fact]
    public async Task StartsANewSessionWithANoticeWhenTheResumedFileNoLongerExists()
    {
        var first = Create();
        await first.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        var file = first.Service.Session.SessionFile!;
        await first.Service.StopAsync();
        File.Delete(file);
        var second = Create();
        var before = SentCommands().Count;
        await second.Service.StartAsync(new StartOptions { ResumeSessionFile = file }, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, second.Service.Connection.State);
        Assert.Contains(SentCommands().Skip(before), c => (string?)c["type"] == "new_session");
        Assert.NotEqual(file, second.Service.Session.SessionFile);
        Assert.Contains("no longer exists", second.Logger.Text("warn"));
        Assert.Contains(second.Notices(), n => n.StartsWith("warning: ") && n.Contains("no longer exists"));
    }

    [Fact]
    public async Task TellsTheUserWhenAnOmpExtensionVetoesResumingTheSessionAtStart()
    {
        var first = Create();
        await first.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        await first.Service.PromptAsync("remember me");
        var file = first.Service.Session.SessionFile!;
        await first.Service.StopAsync();
        var second = Create(env: new Dictionary<string, string> { ["FAKE_OMP_CANCEL_SWITCH"] = "1" });
        await second.Service.StartAsync(new StartOptions { ResumeSessionFile = file }, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, second.Service.Connection.State);
        Assert.NotEqual(file, second.Service.Session.SessionFile);
        Assert.Contains(second.Notices(), n => n.StartsWith("warning: Resuming ") && n.Contains("was cancelled by an OMP extension"));
    }

    [Fact]
    public async Task LoadsLongHistoriesPageByPage()
    {
        var first = Create();
        await first.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken);
        foreach (var text in new[] { "one", "two", "three" }) await first.Service.PromptAsync(text);
        var file = first.Service.Session.SessionFile!;
        await first.Service.StopAsync();
        var second = Create(tune: t => t.HistoryPageLimit = 2);
        var before = SentCommands().Count;
        await second.Service.StartAsync(new StartOptions { ResumeSessionFile = file }, TestContext.Current.CancellationToken);
        var pages = SentCommands().Skip(before).Where(c => (string?)c["type"] == "get_messages_page").ToArray();
        Assert.Equal(new[] { "2:", "2:2", "2:4" }, pages.Select(p => $"{p["limit"]}:{p["cursor"]}"));
        Assert.Equal(6, second.Service.Transcript.Count);
    }

    [Fact]
    public async Task RestartDuringAnInFlightStartEndsReadyAndRejectsTheSupersededStart()
    {
        var h = Create();
        var start = Wait.Settle(h.Service.StartAsync(new StartOptions { NewSession = true }, TestContext.Current.CancellationToken));
        await h.Service.RestartAsync();
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
        Assert.True(FakeOmp.IsAlive(h.Service.Connection.Pid!.Value));
        Assert.IsType<OmpSupersededException>(await start);
    }

    [Fact]
    public async Task OverlappingRestartsEndWithOneReadyProcess()
    {
        var h = Create();
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(Wait.Settle(h.Service.RestartAsync()), Wait.Settle(h.Service.RestartAsync()));
        foreach (var error in results) Assert.True(error == null || error is OmpSupersededException, error?.ToString());
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
    }

    [Fact]
    public async Task StopDuringAnInFlightStartRejectsTheStartAsSupersededAndStaysStopped()
    {
        var h = Create();
        var start = Wait.Settle(h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        await h.Service.StopAsync();
        Assert.IsType<OmpSupersededException>(await start);
        Assert.Equal(ConnectionState.Stopped, h.Service.Connection.State);
        Assert.DoesNotContain(ConnectionState.Failed, h.Locked(() => h.States.ToArray()));
    }

    [Fact]
    public async Task CancellingTheStartTokenStopsTheLaunch()
    {
        var h = Create();
        using var cancellation = new CancellationTokenSource();
        var start = Wait.Settle(h.Service.StartAsync(cancellationToken: cancellation.Token));
        await Task.Delay(30, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.IsAssignableFrom<OperationCanceledException>(await start);
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Stopped, 5000, "stopped");
    }

    [Fact]
    public async Task StartWhileARestartIsPendingCancelsThePendingRestart()
    {
        var h = Create(autoRestart: true, tune: t => t.RestartDelaysMs = new[] { 300, 300, 300 });
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await h.Service.PromptAsync("please crash");
        await Wait.For(() => h.Service.Connection.State == ConnectionState.Restarting, 10000, "restarting");
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var pid = h.Service.Connection.Pid;
        await Task.Delay(600, TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
        Assert.Equal(pid, h.Service.Connection.Pid);
    }

    [Fact]
    public async Task RecoversWhenTheOmpProcessCannotBeCreated()
    {
        var calls = 0;
        var h = Create(tune: t => t.Spawn = options =>
        {
            if (++calls == 1) throw new PathTooLongException("spawn ENAMETOOLONG");
            return new OmpProcess(options);
        });
        Assert.Contains("ENAMETOOLONG", (await Assert.ThrowsAsync<PathTooLongException>(() => h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken))).Message);
        Assert.Equal(ConnectionState.Failed, h.Service.Connection.State);
        await h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, h.Service.Connection.State);
    }

    [Fact]
    public async Task FailsStartWithAClearErrorWhenTheExecutableCannotBeSpawned()
    {
        var h = Create(autoRestart: true, executable: Path.Combine(_dir, "missing.exe"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => h.Service.StartAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("missing.exe", error.Message);
        Assert.Equal(ConnectionState.Failed, h.Service.Connection.State);
        Assert.Contains("missing.exe", h.Service.Connection.Detail);
    }
}
