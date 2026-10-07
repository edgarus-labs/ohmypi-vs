using Newtonsoft.Json.Linq;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Service;

public sealed class OmpServiceGapsTests : IAsyncLifetime
{
    private sealed class Tools : IHostTools
    {
        public IReadOnlyList<HostToolDefinition> Definitions { get; } = new[] { new HostToolDefinition("vs_ping", "Pings", new JObject()) };

        public Task<HostToolResult> InvokeAsync(string name, JObject arguments, CancellationToken cancellationToken) => Task.FromResult(HostToolResult.Text("pong"));
    }

    private readonly List<OmpService> _services = new();

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        foreach (var service in _services)
        {
            await Wait.Settle(service.StopAsync());
        }
    }

    private (OmpService Service, MemoryLogger Logger, List<InteractionRequest> Interactions) Create(MemoryOmp omp, IHostTools? tools = null)
    {
        var logger = new MemoryLogger();
        var service = new OmpService(
            new OmpServiceOptions { Executable = "memory-omp", Cwd = Path.GetTempPath(), Logger = logger, HostTools = tools },
            new OmpServiceTuning { Spawn = _ => omp, ShutdownGraceMs = 50 });
        _services.Add(service);
        var interactions = new List<InteractionRequest>();
        service.InteractionRequested += (_, request) => { lock (interactions) { interactions.Add(request); } };

        return (service, logger, interactions);
    }

    private static void Ui(MemoryOmp omp, string id, string method, Action<JObject>? extra = null)
    {
        var frame = new JObject { ["type"] = "extension_ui_request", ["id"] = id, ["method"] = method, ["title"] = "T" };
        extra?.Invoke(frame);
        omp.Emit(frame);
    }

    [Fact]
    public void ThePublicConstructorBuildsAServiceForItsDirectoryWithoutStartingIt()
    {
        var service = new OmpService(new OmpServiceOptions { Executable = "omp", Cwd = "C:\\repo", Logger = new MemoryLogger() });
        Assert.Equal("C:\\repo", service.Cwd);
        Assert.Equal(ConnectionState.Stopped, service.Connection.State);
        service.Dispose();
    }

    [Fact]
    public async Task CancellingAnInteractionSendsCancelledAndAnUnknownResponseKindIsRefused()
    {
        var omp = new MemoryOmp();
        var (service, _, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Ui(omp, "ui-1", "confirm");
        await Wait.For(() => interactions.Count > 0, 1000, "confirm request");
        service.RespondInteraction("ui-1", InteractionResponse.Cancelled());
        var sent = Assert.Single(omp.Sent("extension_ui_response"));
        Assert.True((bool)sent["cancelled"]!);

        Ui(omp, "ui-2", "confirm");
        await Wait.For(() => interactions.Count > 1, 1000, "second request");
        Assert.Throws<ArgumentException>(() => service.RespondInteraction("ui-2", new UnknownResponse()));
    }

    private sealed class UnknownResponse : InteractionResponse
    {
    }

    [Fact]
    public async Task AnAskWithoutQuestionsAndAnUnknownMethodAreCancelledOrIgnored()
    {
        var omp = new MemoryOmp();
        var (service, logger, interactions) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Ui(omp, "ask-1", "ask", f => f["questions"] = new JArray());
        Ui(omp, "x-1", "teleport");
        await Wait.For(() => omp.Sent("extension_ui_response").Count() == 1, 1000, "cancel of the ask");
        Assert.True((bool)omp.Sent("extension_ui_response").Single()["cancelled"]!);
        Assert.Empty(interactions);
        Assert.Contains("Unsupported OMP UI method teleport ignored", logger.Text());
    }

    [Fact]
    public async Task HostToolsOmpRefusesAreLoggedAndTheServiceStillStarts()
    {
        var omp = new MemoryOmp(new() { ["set_host_tools"] = (f, o) => { o.Fail(f, "no such command", "unsupported"); return MemoryOmp.NoReply; } });
        var (service, logger, _) = Create(omp, new Tools());
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Ready, service.Connection.State);
        Assert.Contains("OMP did not accept the Visual Studio tools (unsupported): no such command", logger.Text("warn"));
    }

    [Fact]
    public async Task AnEffortSelectorOmpCannotReadFromTheJournalFallsBackToTheEffectiveLevel()
    {
        var omp = new MemoryOmp(new()
        {
            ["get_state"] = (_, _) => new JObject { ["sessionId"] = "s", ["thinkingLevel"] = "high", ["isStreaming"] = false, ["isCompacting"] = false, ["messageCount"] = 0 },
            ["get_entries"] = (f, o) => { o.Fail(f, "journal unreadable", "io"); return MemoryOmp.NoReply; },
        });
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Wait.For(() => service.Session.ThinkingLevel == "high", 1000, "effective level");
        Assert.Contains("Reading the effort selector from the session journal failed", logger.Text("warn"));
    }

    [Fact]
    public async Task FramesOmpSendsThatNeedNoActionAreLoggedAndIgnored()
    {
        var omp = new MemoryOmp();
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "host_tool_cancel", ["id"] = "1" });
        omp.Emit(new JObject { ["type"] = "host_uri_cancel", ["id"] = "2" });
        omp.Emit(new JObject { ["type"] = "extension_error", ["extensionPath"] = "ext.ts", ["event"] = "start", ["error"] = "bad" });
        omp.Emit(new JObject { ["type"] = "rpc_frame_error", ["originalType"] = "get_messages", ["error"] = "too big" });
        omp.Emit(new JObject { ["type"] = "rpc_frame_error", ["error"] = "too big" });
        omp.Emit(new JObject { ["type"] = "brand_new_frame" });
        omp.Emit(new JObject { ["id"] = "no-type" });
        await Wait.For(() => logger.Text().Contains("Unknown OMP frame type brand_new_frame ignored") && logger.Text().Contains("without a type"), 1000, "frames logged");
        Assert.Contains("OMP extension error (ext.ts, start): bad", logger.Text("warn"));
        Assert.Contains("OMP dropped an oversized get_messages: too big", logger.Text("warn"));
        Assert.Contains("OMP dropped an oversized frame: too big", logger.Text("warn"));
        Assert.Contains("OMP host_tool_cancel ignored", logger.Text());
        Assert.Contains("OMP host_uri_cancel ignored", logger.Text());
    }

    [Fact]
    public async Task ASecondReadyFrameIsIgnored()
    {
        var omp = new MemoryOmp();
        var (service, logger, _) = Create(omp);
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
        omp.Emit(new JObject { ["type"] = "ready", ["protocolVersion"] = 1 });
        await Wait.For(() => logger.Text().Contains("Duplicate OMP ready frame ignored"), 1000, "duplicate ready logged");
    }
}
