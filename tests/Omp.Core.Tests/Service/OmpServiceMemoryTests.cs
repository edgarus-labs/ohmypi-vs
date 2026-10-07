using Newtonsoft.Json.Linq;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Service;

public sealed class OmpServiceMemoryTests : IAsyncLifetime
{
    private readonly List<OmpService> _services = new();

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _services)
        {
            await Wait.Settle(service.StopAsync());
        }
    }

    private (OmpService Service, MemoryLogger Logger, List<InteractionRequest> Interactions) Create(MemoryOmp omp, bool autoRestart = false)
    {
        var logger = new MemoryLogger();
        var service = new OmpService(
            new OmpServiceOptions { Executable = "memory-omp", Cwd = Path.GetTempPath(), Logger = logger, AutoRestart = autoRestart },
            new OmpServiceTuning { Spawn = _ => omp, ShutdownGraceMs = 50 });
        _services.Add(service);
        var interactions = new List<InteractionRequest>();
        service.InteractionRequested += (_, request) => { lock (interactions) { interactions.Add(request); } };

        return (service, logger, interactions);
    }

    private static JObject User(string text) => new() { ["role"] = "user", ["content"] = text, ["timestamp"] = 1 };

    private static IEnumerable<string> Notices(OmpService service) =>
        service.Transcript.OfType<NoticeItem>().Select(n => $"{n.Level}: {n.Text}");

    [Fact]
    public async Task ConstructsWithoutIoAndTreatsStopAndDisposeOfANeverStartedServiceAsNoOps()
    {
        var spawned = false;
        var changes = 0;
        var service = new OmpService(
            new OmpServiceOptions { Executable = "omp", Cwd = "C:\\nowhere", Logger = new MemoryLogger() },
            new OmpServiceTuning { Spawn = _ => { spawned = true; return new MemoryOmp(); } });
        service.ConnectionChanged += (_, _) => changes++;
        Assert.Equal(ConnectionState.Stopped, service.Connection.State);
        await service.StopAsync();
        service.Dispose();
        Assert.False(spawned);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task StartsConfiguresAndReportsReadyWithTheNegotiatedProtocol()
    {
        var omp = new MemoryOmp();
        var (service, _, _) = Create(omp);
        var states = new List<ConnectionState>();
        service.ConnectionChanged += (_, c) => states.Add(c.State);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, service.Connection.State);
        Assert.Equal(2, service.Connection.ProtocolVersion);
        Assert.Equal(omp.Pid, service.Connection.Pid);
        Assert.Equal(new[] { ConnectionState.Starting, ConnectionState.Ready }, states);
        Assert.Equal("progress", (string?)omp.Sent("set_subagent_subscription").Single()["level"]);
        Assert.True((bool)omp.Sent("set_ask_dialog").Single()["enabled"]!);
        Assert.Equal("delta", (string?)omp.Sent("set_event_filter").Single()["messageUpdates"]);
        Assert.Equal(JTokenType.Null, omp.Sent("set_event_filter").Single()["events"]!.Type);
        Assert.Equal("memory-session", service.Session.SessionId);
    }

    [Fact]
    public async Task PublishesTheSlashCommandsOmpAnnounces()
    {
        var omp = new MemoryOmp();
        var (service, _, _) = Create(omp);
        var published = new List<IReadOnlyList<SlashCommandView>>();
        service.CommandsChanged += (_, commands) => { lock (published) { published.Add(commands); } };
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(JObject.Parse("""
            {"type":"available_commands_update","commands":[
              {"name":"model","aliases":["models"],"description":"Show current model selection","source":"builtin"},
              {"name":"security","description":"Scans","input":{"hint":"<plan|scan>"},"subcommands":[{"name":"plan"}]},
              {"description":"nameless entries are dropped"}
            ]}
            """));
        await Wait.For(() => service.Commands.Count == 2, 1000, "commands");
        var model = service.Commands[0];
        Assert.Equal(("model", "Show current model selection", "builtin"), (model.Name, model.Description, model.Source));
        Assert.Equal(new[] { "models" }, model.Aliases);
        Assert.Null(model.Hint);
        Assert.Equal(("security", "<plan|scan>"), (service.Commands[1].Name, service.Commands[1].Hint));
        lock (published)
        {
            Assert.Same(service.Commands, published.Last());
        }
    }

    [Fact]
    public async Task KeepsAnInteractionPendingWhenItsAnswerCannotBeWritten()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-1", ["method"] = "confirm", ["title"] = "Go?", ["message"] = "Really?" });
        await Wait.For(() => interactions.Count > 0, 1000, "confirm request");
        var confirm = Assert.IsType<ConfirmRequest>(interactions[0]);
        Assert.Equal(("Go?", "Really?"), (confirm.Title, confirm.Message));
        omp.FailWrites = true;
        Assert.Throws<IOException>(() => service.RespondInteraction("ui-1", InteractionResponse.FromConfirmed(true)));
        omp.FailWrites = false;
        service.RespondInteraction("ui-1", InteractionResponse.FromConfirmed(true));
        var sent = Assert.Single(omp.Sent("extension_ui_response"));
        Assert.Equal("{\"type\":\"extension_ui_response\",\"id\":\"ui-1\",\"confirmed\":true}", sent.ToString(Newtonsoft.Json.Formatting.None));
        service.RespondInteraction("ui-1", InteractionResponse.FromConfirmed(false));
        Assert.Single(omp.Sent("extension_ui_response"));
    }

    [Fact]
    public async Task ExposesPendingInteractionsUntilTheyAreAnsweredOrWithdrawn()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-1", ["method"] = "confirm", ["title"] = "Go?" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-2", ["method"] = "input", ["title"] = "Name" });
        await Wait.For(() => interactions.Count == 2, 1000, "two requests");
        Assert.Equal(new[] { "ui-1", "ui-2" }, service.PendingInteractions.Select(r => r.Id));
        service.RespondInteraction("ui-1", InteractionResponse.FromConfirmed(true));
        Assert.Equal(new[] { "ui-2" }, service.PendingInteractions.Select(r => r.Id));
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "c-1", ["method"] = "cancel", ["targetId"] = "ui-2" });
        await Wait.For(() => service.PendingInteractions.Count == 0, 1000, "withdrawn");
    }

    [Fact]
    public async Task CancelsAUiRequestItCannotMapAndTellsTheUser()
    {
        var omp = new MemoryOmp();
        var (service, logger, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-2", ["method"] = "select", ["title"] = "Pick one" });
        await Wait.For(() => omp.Sent("extension_ui_response").Count > 0, 1000, "cancel reply");
        Assert.Equal("{\"type\":\"extension_ui_response\",\"id\":\"ui-2\",\"cancelled\":true}", omp.Sent("extension_ui_response")[0].ToString(Newtonsoft.Json.Formatting.None));
        Assert.Empty(interactions);
        Assert.Contains("select", logger.Text("warn"));
        Assert.Contains(Notices(service), n => n.StartsWith("Warning: ") && n.Contains("select"));
    }

    [Fact]
    public async Task MarksSecretInputsAndNeverTracesAnyInputAnswer()
    {
        var omp = new MemoryOmp();
        var (service, logger, interactions) = Create(omp);
        logger.TraceEnabled = true;
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-3", ["method"] = "input", ["title"] = "API key", ["password"] = true, ["timeout"] = 5000 });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-4", ["method"] = "input", ["title"] = "Name" });
        await Wait.For(() => interactions.Count == 2, 1000, "input requests");
        var secret = Assert.IsType<InputRequest>(interactions[0]);
        Assert.True(secret.Secret);
        Assert.Equal(5000, secret.TimeoutMs);
        Assert.False(Assert.IsType<InputRequest>(interactions[1]).Secret);
        service.RespondInteraction("ui-3", InteractionResponse.FromValue("hunter2-token"));
        service.RespondInteraction("ui-4", InteractionResponse.FromValue("visible-name"));
        Assert.Equal("hunter2-token", (string?)omp.Sent("extension_ui_response")[0]["value"]);
        Assert.DoesNotContain(logger.Records, r => r.Message.Contains("hunter2-token"));
        Assert.DoesNotContain("visible-name", logger.Text("trace"));
        Assert.Contains("[redacted]", logger.Text("trace"));
    }

    [Fact]
    public async Task IgnoresFramesFromAProcessThatHasAlreadyExited()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Close(3, omp.Pid);
        await Wait.For(() => service.Connection.State == ConnectionState.Failed, 1000, "failed");
        var before = service.Transcript.Count;
        omp.Emit(new JObject { ["type"] = "message_start", ["messageId"] = "late", ["message"] = User("late frame") });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-late", ["method"] = "confirm", ["title"] = "Late?", ["message"] = "late" });
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Equal(before, service.Transcript.Count);
        Assert.Empty(interactions);
    }

    [Fact]
    public async Task EndsFailedWhenTheProcessCannotBeShutDown()
    {
        var omp = new MemoryOmp();
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.ShutdownError = new IOException("OMP process 7 did not exit after its process tree was killed");
        var error = await Assert.ThrowsAsync<IOException>(() => service.StopAsync());
        Assert.Contains("did not exit", error.Message);
        Assert.Equal(ConnectionState.Failed, service.Connection.State);
        Assert.Contains("did not exit", service.Connection.Detail);
        Assert.DoesNotContain(logger.Records, r => r.Level == "error");
        omp.ShutdownError = null;
    }

    [Fact]
    public async Task FallsBackToGetMessagesWhenMessagePagesChangeWhileLoading()
    {
        var history = new JArray(User("a"), User("b"), User("c"), User("d"));
        var page = 0;
        var omp = new MemoryOmp(new()
        {
            ["get_messages_page"] = (_, _) => ++page == 1
                ? new JObject { ["messages"] = new JArray(history[0]), ["totalMessages"] = 3, ["nextCursor"] = "1" }
                : new JObject { ["messages"] = new JArray(history[1]), ["totalMessages"] = 4, ["nextCursor"] = "2" },
            ["get_messages"] = (_, _) => new JObject { ["messages"] = history },
        });
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "a", "b", "c", "d" }, service.Transcript.OfType<UserItem>().Select(u => u.Text));
        Assert.Equal(2, omp.Sent("get_messages_page").Count);
        Assert.Contains("changed while loading", logger.Text("debug"));
    }

    [Fact]
    public async Task LogsAnEventFilterOmpDoesNotSupportAsOneLineWithoutTheStack()
    {
        var omp = new MemoryOmp(new()
        {
            ["set_event_filter"] = (frame, transport) =>
            {
                transport.Fail(frame, "no filter", "unsupported");

                return MemoryOmp.NoReply;
            },
        });
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var warning = Assert.Single(logger.Records, r => r.Level == "warn" && r.Message.Contains("event filter"));
        Assert.Contains("no filter", warning.Message);
        Assert.Null(warning.Error);
    }

    [Fact]
    public async Task PromptResolvesAtPromptResultNotAtTheAcknowledgement()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = service.PromptAsync("hello");
        await Wait.For(() => omp.Sent("prompt").Count == 1 && service.Session.Phase == SessionPhase.Running, 1000, "admitted");
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(outcome.IsCompleted);
        var id = (string)omp.Sent("prompt")[0]["id"]!;
        omp.Emit(new JObject { ["type"] = "prompt_result", ["id"] = id, ["agentInvoked"] = true, ["status"] = "error", ["sessionSettled"] = true, ["error"] = new JObject { ["message"] = "rate limited", ["retryable"] = true } });
        var result = await outcome;
        Assert.Equal((PromptStatus.Error, "rate limited", true, true), (result.Status, result.Error, result.SessionSettled, result.Admitted));
        Assert.Equal("rate limited", service.Session.LastError);
        Assert.Equal(SessionPhase.Idle, service.Session.Phase);
    }

    [Fact]
    public async Task SteersOrQueuesAFollowUpWhileTheAgentIsBusy()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        _ = service.PromptAsync("first");
        await Wait.For(() => service.Session.Phase == SessionPhase.Running, 1000, "running");
        _ = service.PromptAsync("steer me");
        _ = service.PromptAsync("later", PromptMode.FollowUp);
        var prompts = omp.Sent("prompt");
        Assert.Null(prompts[0]["streamingBehavior"]);
        Assert.Equal("steer", (string?)prompts[1]["streamingBehavior"]);
        Assert.Equal("followUp", (string?)prompts[2]["streamingBehavior"]);
    }

    [Fact]
    public async Task ShowsThePromptAtOnceAndLetsOmpsUserMessageTakeOverTheSameRow()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => MemoryOmp.NoReply });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        _ = service.PromptAsync("hello there");
        var echo = Assert.IsType<UserItem>(Assert.Single(service.Transcript));
        Assert.Equal("hello there", echo.Text);

        omp.Emit(new JObject { ["type"] = "message_start", ["messageId"] = "msg-1", ["message"] = new JObject { ["role"] = "user", ["content"] = "hello there (expanded)", ["timestamp"] = 1 } });
        await Wait.For(() => service.Transcript.OfType<UserItem>().Any(u => u.Text.EndsWith("(expanded)")), 1000, "omp user message");
        omp.Emit(new JObject { ["type"] = "message_end", ["messageId"] = "msg-1", ["message"] = new JObject { ["role"] = "user", ["content"] = "hello there (expanded)", ["timestamp"] = 1 } });
        await Task.Delay(30, TestContext.Current.CancellationToken);
        var user = Assert.IsType<UserItem>(Assert.Single(service.Transcript));
        Assert.Equal(echo.Id, user.Id);
    }

    [Fact]
    public async Task KeepsTheEchoOfASlashCommandOmpRunsWithoutTheAgent()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => new JObject { ["agentInvoked"] = false } });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = await service.PromptAsync("/session");
        Assert.Equal(PromptStatus.Local, outcome.Status);
        Assert.Equal("/session", Assert.IsType<UserItem>(Assert.Single(service.Transcript)).Text);
    }

    [Fact]
    public async Task RemovesTheEchoOfARefusedPrompt()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (f, o) => { o.Fail(f, "model not configured"); return MemoryOmp.NoReply; } });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var resets = new List<IReadOnlyList<TranscriptItem>>();
        service.TranscriptReset += (_, items) => resets.Add(items);
        var outcome = await service.PromptAsync("hi");
        Assert.Equal(PromptStatus.Error, outcome.Status);
        Assert.Empty(service.Transcript.OfType<UserItem>());
        Assert.Empty(resets.Last().OfType<UserItem>());
        Assert.False(outcome.Admitted);
    }

    [Fact]
    public async Task DoesNotEchoSteeringOrFollowUpsWhichOmpQueues()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        _ = service.PromptAsync("first");
        await Wait.For(() => service.Session.Phase == SessionPhase.Running, 1000, "running");
        _ = service.PromptAsync("steer me");
        _ = service.PromptAsync("later", PromptMode.FollowUp);
        Assert.Equal(new[] { "first" }, service.Transcript.OfType<UserItem>().Select(u => u.Text));
    }

    private static JObject AssistantEnd(string id, long input, long output, long cacheRead, double? cost) => new()
    {
        ["type"] = "message_end",
        ["messageId"] = id,
        ["message"] = new JObject
        {
            ["role"] = "assistant",
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = "ok" }),
            ["usage"] = new JObject
            {
                ["input"] = input,
                ["output"] = output,
                ["cacheRead"] = cacheRead,
                ["cacheWrite"] = 0,
                ["cost"] = cost is null ? null : new JObject { ["total"] = cost },
            },
            ["timestamp"] = 1,
        },
    };

    [Fact]
    public async Task EndsEachAgentTurnWithItsDurationTokensAndCost()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = service.PromptAsync("do it");
        await Wait.For(() => omp.Sent("prompt").Count == 1, 1000, "prompt sent");
        omp.Emit(AssistantEnd("a1", 100, 20, 1000, 0.01));
        omp.Emit(AssistantEnd("a2", 50, 30, 2000, 0.02));
        await Wait.For(() => service.Transcript.OfType<AssistantItem>().Count() == 2, 1000, "assistant messages");
        await Task.Delay(20, TestContext.Current.CancellationToken);
        var id = (string)omp.Sent("prompt")[0]["id"]!;
        omp.Emit(new JObject { ["type"] = "prompt_result", ["id"] = id, ["agentInvoked"] = true, ["status"] = "completed", ["sessionSettled"] = true });
        await outcome;
        var summary = Assert.IsType<TurnSummaryItem>(service.Transcript.Last());
        Assert.Equal((150L, 50L, 3000L), (summary.InputTokens, summary.OutputTokens, summary.CacheReadTokens));
        Assert.Equal(0.03, summary.CostUsd!.Value, 6);
        Assert.InRange(summary.DurationMs, 20, 10_000);
        Assert.False(summary.Aborted);
    }

    [Fact]
    public async Task MarksAnAbortedTurnAndLeavesCostUnknownWithoutPricing()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = service.PromptAsync("do it");
        await Wait.For(() => omp.Sent("prompt").Count == 1, 1000, "prompt sent");
        omp.Emit(AssistantEnd("a1", 10, 5, 0, null));
        await Wait.For(() => service.Transcript.OfType<AssistantItem>().Any(), 1000, "assistant message");
        var id = (string)omp.Sent("prompt")[0]["id"]!;
        omp.Emit(new JObject { ["type"] = "prompt_result", ["id"] = id, ["agentInvoked"] = true, ["status"] = "aborted", ["sessionSettled"] = true });
        await outcome;
        var summary = Assert.IsType<TurnSummaryItem>(service.Transcript.Last());
        Assert.True(summary.Aborted);
        Assert.Null(summary.CostUsd);
    }

    [Fact]
    public async Task AddsNoTurnSummaryForASlashCommandOmpRunsWithoutTheAgent()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => new JObject { ["agentInvoked"] = false } });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await service.PromptAsync("/session");
        Assert.Empty(service.Transcript.OfType<TurnSummaryItem>());
    }

    [Fact]
    public async Task RejectsCommandsWhenOmpIsNotRunning()
    {
        var (service, _, _) = Create(new MemoryOmp());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PromptAsync("hi"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SteerAgentAsync("main", "go left"));
        Assert.Contains("not running", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListModelsAsync());
    }

    [Fact]
    public async Task ForwardsPresentationRequests()
    {
        var omp = new MemoryOmp();
        var (service, _, _) = Create(omp);
        var presentations = new List<PresentationRequest>();
        service.Presentation += (_, p) => presentations.Add(p);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "1", ["method"] = "notify", ["message"] = "Heads up", ["notifyType"] = "warning" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "2", ["method"] = "setStatus", ["statusKey"] = "k", ["statusText"] = "busy" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "3", ["method"] = "setStatus", ["statusKey"] = "k" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "4", ["method"] = "open_url", ["url"] = "https://a.invalid", ["launchUrl"] = "https://b.invalid", ["instructions"] = "Sign in" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "5", ["method"] = "set_editor_text", ["text"] = "draft" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "6", ["method"] = "setWidget", ["widgetKey"] = "w" });
        Assert.Equal(new[] { "notify Warning Heads up", "status k busy", "status k ", "url https://b.invalid Sign in", "editor draft" }, presentations.Select(p => p switch
        {
            NotifyPresentation n => $"notify {n.Level} {n.Message}",
            StatusPresentation s => $"status {s.Key} {s.Text}",
            OpenUrlPresentation u => $"url {u.Url} {u.Instructions}",
            EditorTextPresentation e => $"editor {e.Text}",
            _ => "?",
        }));
    }

    private (OmpService Service, MemoryLogger Logger, List<MemoryOmp> Spawned) CreateRestarting(int[] delays)
    {
        var logger = new MemoryLogger();
        var spawned = new List<MemoryOmp>();
        var service = new OmpService(
            new OmpServiceOptions { Executable = "memory-omp", Cwd = Path.GetTempPath(), Logger = logger, AutoRestart = true },
            new OmpServiceTuning
            {
                RestartDelaysMs = delays,
                ShutdownGraceMs = 50,
                Spawn = _ =>
                {
                    var omp = new MemoryOmp();
                    lock (spawned)
                    {
                        spawned.Add(omp);
                    }

                    return omp;
                },
            });
        _services.Add(service);

        return (service, logger, spawned);
    }

    [Fact]
    public async Task LogsTheStderrOfAnOmpThatDiesDuringStartup()
    {
        var omp = new MemoryOmp(new()
        {
            ["set_subagent_subscription"] = (_, o) =>
            {
                o.Close(1, o.Pid, "fatal: config.yml is broken");

                return MemoryOmp.NoReply;
            },
        });
        var (service, logger, _) = Create(omp);
        await Assert.ThrowsAsync<OmpRequestException>(() => service.StartAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("fatal: config.yml is broken", logger.Text("warn"));
    }

    [Fact]
    public async Task KeepsTheSlashCommandsWhenAnUpdateCarriesNoList()
    {
        var omp = new MemoryOmp();
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(JObject.Parse("""{"type":"available_commands_update","commands":[{"name":"model"}]}"""));
        omp.Emit(JObject.Parse("""{"type":"available_commands_update","commands":"broken"}"""));
        Assert.Equal("model", Assert.Single(service.Commands).Name);
        Assert.Contains("available_commands_update", logger.Text("warn"));
    }

    [Fact]
    public async Task KeepsTheEchoOfAnAdmittedPromptThatFailsAndReportsAnErrorWithoutAMessage()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = service.PromptAsync("hello");
        await Wait.For(() => service.Session.Phase == SessionPhase.Running, 1000, "admitted");
        var id = (string)omp.Sent("prompt")[0]["id"]!;
        omp.Emit(new JObject { ["type"] = "prompt_result", ["id"] = id, ["agentInvoked"] = true, ["status"] = "error", ["sessionSettled"] = true });
        var result = await outcome;
        Assert.Equal((PromptStatus.Error, true), (result.Status, result.Admitted));
        Assert.Equal("hello", Assert.Single(service.Transcript.OfType<UserItem>()).Text);
        Assert.False(string.IsNullOrEmpty(service.Session.LastError));
    }

    [Fact]
    public async Task KeepsTheEchoOfAnAdmittedPromptWhoseProcessDies()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => null });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        var outcome = service.PromptAsync("hello");
        await Wait.For(() => service.Session.Phase == SessionPhase.Running, 1000, "admitted");
        omp.Close(3, omp.Pid);
        var result = await outcome;
        Assert.Equal((PromptStatus.Error, true), (result.Status, result.Admitted));
        Assert.Equal("hello", Assert.Single(service.Transcript.OfType<UserItem>()).Text);
    }

    [Fact]
    public async Task SteersTheMainAgentAndFailsWhenOmpRefusesTheSteer()
    {
        var prompts = 0;
        var omp = new MemoryOmp(new()
        {
            ["prompt"] = (frame, o) =>
            {
                if (Interlocked.Increment(ref prompts) < 3)
                {
                    return null;
                }

                o.Fail(frame, "steering is not possible now");

                return MemoryOmp.NoReply;
            },
        });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        _ = service.PromptAsync("first");
        await Wait.For(() => service.Session.Phase == SessionPhase.Running, 1000, "running");
        await service.SteerAgentAsync("main", "go left");
        Assert.Equal("steer", (string?)omp.Sent("prompt")[1]["streamingBehavior"]);
        var error = await Assert.ThrowsAsync<OmpRequestException>(() => service.SteerAgentAsync("main", "go right"));
        Assert.Contains("steering is not possible now", error.Message);
    }

    [Fact]
    public async Task DoesNotRestartOmpOnceAStopRacesTheRestart()
    {
        var (service, _, spawned) = CreateRestarting([1]);
        var restarting = 0;
        service.ConnectionChanged += (_, c) =>
        {
            if (c.State == ConnectionState.Restarting && ++restarting == 2)
            {
                _ = service.StopAsync();
            }
        };
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        spawned[0].Close(3, spawned[0].Pid);
        await Wait.For(() => restarting == 2, 2000, "restart attempt");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        lock (spawned)
        {
            Assert.Single(spawned);
        }

        Assert.Equal(ConnectionState.Stopped, service.Connection.State);
    }

    [Fact]
    public async Task BacksOffEachRestartByItsOwnDelayAndSaysSo()
    {
        var (service, _, spawned) = CreateRestarting([10, 30, 50]);
        var details = new List<string>();
        service.ConnectionChanged += (_, c) => { if (c.State == ConnectionState.Restarting && c.Detail!.Contains("; restart")) { lock (details) { details.Add(c.Detail); } } };
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
        {
            var current = spawned.Last();
            current.Close(3, current.Pid);
            await Wait.For(() => service.Connection.State == ConnectionState.Ready && spawned.Count == i + 2, 2000, $"restart {i + 1}");
        }
        lock (details)
        {
            Assert.Equal(new[] { "restart 1/3 in 10 ms", "restart 2/3 in 30 ms", "restart 3/3 in 50 ms" }, details.Select(d => d.Substring(d.IndexOf("restart ", StringComparison.Ordinal))));
        }
    }

    [Fact]
    public async Task LogsAStateRefreshThatFailsUnexpectedly()
    {
        var states = 0;
        var omp = new MemoryOmp(new()
        {
            ["get_state"] = (_, _) => ++states == 1
                ? new JObject { ["sessionId"] = "memory-session" }
                : new JObject { ["sessionId"] = "memory-session", ["contextUsage"] = new JObject { ["tokens"] = JToken.Parse("100000000000000000000000000") } },
        });
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "config_update" });
        await Wait.For(() => logger.Text("error").Contains("Refreshing OMP state failed"), 2000, "logged refresh failure");
    }

    [Fact]
    public async Task TellsTheUserWhenAnAnswerOrAnAutomaticCancelNeverReachedOmp()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.FailWrites = true;
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-1", ["method"] = "select", ["title"] = "Pick" });
        Assert.Contains(Notices(service), n => n.StartsWith("Error: ") && n.Contains("ui-1") && n.Contains("EPIPE"));
        Assert.DoesNotContain(Notices(service), n => n.Contains("it was cancelled"));
        omp.FailWrites = false;
        omp.FailWritesLater = new IOException("pipe is being closed");
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-2", ["method"] = "confirm", ["title"] = "Go?" });
        await Wait.For(() => interactions.Count == 1, 1000, "confirm request");
        service.RespondInteraction("ui-2", InteractionResponse.FromConfirmed(true));
        await Wait.For(() => Notices(service).Any(n => n.Contains("ui-2") && n.Contains("pipe is being closed")), 1000, "undelivered answer notice");
    }

    [Fact]
    public async Task NamesTheEventWhoseListenerFailed()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => MemoryOmp.NoReply });
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        service.TranscriptItemChanged += (_, _) => throw new InvalidOperationException("listener bug");
        _ = service.PromptAsync("hi");
        Assert.Contains("TranscriptItemChanged", logger.Text("error"));
    }

    [Fact]
    public async Task SendsNothingForAnInteractionOmpWithdrew()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "ui-1", ["method"] = "input", ["title"] = "Name" });
        omp.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "c-1", ["method"] = "cancel", ["targetId"] = "ui-1" });
        service.RespondInteraction("ui-1", InteractionResponse.FromValue("late"));
        Assert.Empty(omp.Sent("extension_ui_response"));
    }

    [Fact]
    public async Task DisposingARunningServiceShutsItsProcessDown()
    {
        var omp = new MemoryOmp();
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        service.Dispose();
        await Wait.For(() => omp.IsClosed, 1000, "process shut down");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private const string UsageText = "```\nUsage (0s ago)\n\nAnthropic\n- Claude 5 Hour\n  user@example.com: 4.00% used (96.0% left)\n  resets in 4h\n```";

    [Fact]
    public async Task UsageAsksOmpForItsUsageCommandAndKeepsTheReportOutOfTheTranscript()
    {
        string? sent = null;
        var omp = new MemoryOmp(new()
        {
            ["prompt"] = (f, o) =>
            {
                sent = (string?)f["message"];
                o.Emit(new JObject { ["type"] = "command_output", ["text"] = UsageText });

                return new JObject();
            },
        });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        var usage = await service.GetUsageAsync();

        Assert.Equal("/usage", sent);
        var provider = Assert.Single(usage);
        Assert.Equal("Anthropic", provider.Provider);
        Assert.Equal(4.0, Assert.Single(provider.Limits).UsedPercent);
        Assert.Empty(service.Transcript.OfType<CommandOutputItem>());
    }

    [Fact]
    public async Task CommandOutputOutsideAUsageRequestStillReachesTheTranscript()
    {
        var omp = new MemoryOmp(new()
        {
            ["prompt"] = (_, o) =>
            {
                o.Emit(new JObject { ["type"] = "command_output", ["text"] = UsageText });

                return new JObject();
            },
        });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await service.GetUsageAsync();

        omp.Emit(new JObject { ["type"] = "command_output", ["text"] = "hello" });

        await Wait.For(() => service.Transcript.OfType<CommandOutputItem>().Any(item => item.Text == "hello"), 1000, "command output in the transcript");
    }

    [Fact]
    public async Task UsageFailsWhenOmpPrintsNoReport()
    {
        var omp = new MemoryOmp(new() { ["prompt"] = (_, _) => new JObject() });
        var (service, _, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUsageAsync());
    }
}
