using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;
using Omp.Core.Tests.Support;
using System.Text;

namespace Omp.Core.Tests.Protocol;

public class OmpRpcClientTests
{
    private static MemoryTransport Negotiating(Action<JObject, MemoryTransport>? extra = null) =>
        new((frame, t) =>
        {
            if ((string?)frame["type"] == "negotiate_protocol")
            {
                t.Reply(frame, new JObject { ["protocolVersion"] = 2 });
            }
            else
            {
                extra?.Invoke(frame, t);
            }
        });

    private static JObject Ready(int? maxFrameBytes = null)
    {
        var ready = (JObject)MemoryTransport.ReadyV2.DeepClone();
        if (maxFrameBytes is not null)
        {
            ready["maxFrameBytes"] = maxFrameBytes;
        }

        return ready;
    }

    [Fact]
    public async Task WaitsForReadyAndNegotiatesProtocolV2WhenAdvertised()
    {
        var transport = Negotiating();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        var started = client.StartAsync();
        transport.Emit(Ready());
        await started;
        Assert.Equal(2, client.ProtocolVersion);
        Assert.Equal(new[] { 2 }, transport.Sent("negotiate_protocol").Select(f => (int)f["protocolVersion"]!));
    }

    [Fact]
    public async Task StaysOnV1WithoutNegotiatingWhenTheServerDoesNotAdvertiseV2()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        transport.Emit(new JObject { ["type"] = "ready" });
        await client.StartAsync();
        Assert.Equal(1, client.ProtocolVersion);
        Assert.Empty(transport.Sent("negotiate_protocol"));
    }

    [Fact]
    public async Task RejectsStartWhenNoReadyFrameArrivesInTime()
    {
        var client = new OmpRpcClient(new MemoryTransport(), new MemoryLogger(), readyTimeoutMs: 20);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
        Assert.Contains("ready", error.Message);
    }

    [Fact]
    public async Task RejectsStartWhenTheProcessClosesBeforeReady()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger(), readyTimeoutMs: 5000);
        var started = client.StartAsync();
        transport.Close(1);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => started);
        Assert.Contains("exited", error.Message);
    }

    [Fact]
    public async Task CorrelatesOutOfOrderResponsesById()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        transport.Emit(new JObject { ["type"] = "ready" });
        await client.StartAsync();
        var state = client.RequestAsync("get_state");
        var levels = client.RequestAsync("get_available_thinking_levels");
        var stateReq = transport.Written[0];
        var levelsReq = transport.Written[1];
        Assert.NotEqual((string?)stateReq["id"], (string?)levelsReq["id"]);
        transport.Reply(levelsReq, new JObject { ["levels"] = new JArray("off", "high") });
        transport.Reply(stateReq, new JObject { ["sessionId"] = "s1" });
        Assert.Equal("high", (string?)(await levels)!["levels"]![1]);
        Assert.Equal("s1", (string?)(await state)!["sessionId"]);
    }

    [Fact]
    public async Task RejectsFailedCommandsWithOmpRequestExceptionCarryingTheServerCode()
    {
        var transport = new MemoryTransport((frame, t) => t.Fail(frame, "busy", "session_busy"));
        var client = new OmpRpcClient(transport, new MemoryLogger());
        var error = await Assert.ThrowsAsync<OmpRequestException>(() => client.RequestAsync("get_messages_page", new JObject()));
        Assert.Equal("session_busy", error.Code);
        Assert.Equal("get_messages_page", error.Command);
        Assert.Contains("busy", error.Message);
    }

    [Fact]
    public async Task TimesOutRequestsAndIgnoresLateResponses()
    {
        var logger = new MemoryLogger();
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, logger);
        var error = await Assert.ThrowsAsync<OmpRequestException>(() => client.RequestAsync("get_state", timeoutMs: 10));
        Assert.Equal("timeout", error.Code);
        transport.Reply(transport.Written[0], new JObject { ["sessionId"] = "late" });
        Assert.Matches("(?i)late|unknown", logger.Text("debug"));
    }

    [Fact]
    public async Task RejectsPendingRequestsWhenTheTransportCloses()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        var pending = client.RequestAsync("get_state");
        transport.Close(3);
        Assert.Equal("closed", (await Assert.ThrowsAsync<OmpRequestException>(() => pending)).Code);
        Assert.Equal("closed", (await Assert.ThrowsAsync<OmpRequestException>(() => client.RequestAsync("get_state"))).Code);
    }

    [Fact]
    public async Task RejectsWithWriteFailedWhenTheTransportCannotWrite()
    {
        var transport = new MemoryTransport { FailWrites = true };
        var client = new OmpRpcClient(transport, new MemoryLogger());
        Assert.Equal("write_failed", (await Assert.ThrowsAsync<OmpRequestException>(() => client.RequestAsync("get_state"))).Code);
    }

    [Fact]
    public async Task RejectsOnlyTheRequestWhoseWriteTheTransportReportsAsFailedAfterwards()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        transport.FailWritesLater = new IOException("write EPIPE");
        var failed = client.RequestAsync("get_state", timeoutMs: 0);
        transport.FailWritesLater = null;
        var other = client.RequestAsync("get_state", timeoutMs: 0);
        var error = await Assert.ThrowsAsync<OmpRequestException>(() => failed);
        Assert.Equal("write_failed", error.Code);
        Assert.Matches("get_state.*write EPIPE", error.Message);
        transport.Reply(transport.Sent("get_state")[1], new JObject { ["ok"] = true });
        Assert.True((bool)(await other)!["ok"]!);
    }

    [Fact]
    public async Task LogsAUiResponseTheTransportFailsToDeliverAfterwards()
    {
        var transport = new MemoryTransport();
        var logger = new MemoryLogger();
        var client = new OmpRpcClient(transport, logger);
        transport.FailWritesLater = new IOException("write EPIPE");
        client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = "u1", ["confirmed"] = true });
        await Wait.For(() => logger.Text("warn").Length > 0, 1000, "warning");
        Assert.Matches("extension_ui_response.*write EPIPE", logger.Text("warn"));
    }

    [Fact]
    public async Task SendsAnOutboundFrameAboveTheNegotiatedFrameLimitAsOneLineAndWarnsAboutIt()
    {
        var transport = Negotiating();
        var logger = new MemoryLogger();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(Ready(256));
        await client.StartAsync();
        _ = client.RequestAsync("prompt", new JObject { ["message"] = "short" }, timeoutMs: 0);
        Assert.Equal("", logger.Text("warn"));
        _ = client.RequestAsync("prompt", new JObject { ["message"] = new string('x', 300) }, timeoutMs: 0);
        Assert.Equal(2, transport.Sent("prompt").Count);
        Assert.Empty(transport.Sent("rpc_chunk"));
        Assert.Matches(@"prompt frame of \d+ bytes exceeds the 256-byte frame limit OMP advertised", logger.Text("warn"));
    }

    [Fact]
    public async Task DropsInboundLinesAboveTheNegotiatedFrameLimit()
    {
        var transport = Negotiating();
        var logger = new MemoryLogger();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(Ready(256));
        await client.StartAsync();
        var output = new List<string>();
        client.CommandOutput += e => output.Add((string)e["text"]!);
        transport.Emit(new JObject { ["type"] = "command_output", ["text"] = new string('x', 300) });
        transport.Emit(new JObject { ["type"] = "command_output", ["text"] = "ok" });
        Assert.Equal(new[] { "ok" }, output);
        Assert.Contains("exceeds 256 bytes", logger.Text("warn"));
    }

    [Fact]
    public async Task AcceptsInboundLinesUpToANegotiatedFrameLimitAboveTheDefault()
    {
        var transport = Negotiating();
        var logger = new MemoryLogger();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(Ready(12 * 1024 * 1024));
        await client.StartAsync();
        var output = new List<string>();
        client.CommandOutput += e => output.Add((string)e["text"]!);
        var text = new string('x', 9 * 1024 * 1024);
        transport.Emit(new JObject { ["type"] = "command_output", ["text"] = text });
        Assert.Equal(text.Length, output[0].Length);
        Assert.Equal("", logger.Text("warn"));
    }

    [Fact]
    public void DispatchesTypedEventsByFrameCategory()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        var seen = new List<string>();
        client.SessionEvent += e => seen.Add($"session:{e["type"]}");
        client.PromptResult += e => seen.Add($"prompt_result:{e["id"]}");
        client.SessionSettled += () => seen.Add("settled");
        client.UiRequest += e => seen.Add($"ui:{e["method"]}");
        client.SubagentFrame += e => seen.Add($"sub:{e["type"]}");
        client.CommandOutput += e => seen.Add($"out:{e["text"]}");
        client.SessionInfoUpdate += e => seen.Add($"info:{e["title"]}");
        client.ConfigUpdate += _ => seen.Add("config");
        transport.Emit(new JObject { ["type"] = "agent_start" });
        transport.Emit(new JObject { ["type"] = "goal_updated", ["goal"] = null });
        transport.Emit(new JObject { ["type"] = "prompt_result", ["id"] = "p1", ["agentInvoked"] = true, ["status"] = "completed", ["sessionSettled"] = true });
        transport.Emit(new JObject { ["type"] = "session_settled" });
        transport.Emit(new JObject { ["type"] = "extension_ui_request", ["id"] = "u1", ["method"] = "confirm", ["title"] = "t", ["message"] = "m" });
        transport.Emit(new JObject { ["type"] = "subagent_lifecycle", ["payload"] = new JObject { ["id"] = "A", ["status"] = "started" } });
        transport.Emit(new JObject { ["type"] = "command_output", ["text"] = "hi" });
        transport.Emit(new JObject { ["type"] = "session_info_update", ["sessionId"] = "s", ["title"] = "T" });
        transport.Emit(new JObject { ["type"] = "config_update" });
        Assert.Equal(new[]
        {
            "session:agent_start", "session:goal_updated", "prompt_result:p1", "settled", "ui:confirm",
            "sub:subagent_lifecycle", "out:hi", "info:T", "config",
        }, seen);
    }

    [Fact]
    public void ToleratesUnknownFramesAndMalformedLinesWithoutThrowing()
    {
        var logger = new MemoryLogger();
        var transport = new MemoryTransport();
        _ = new OmpRpcClient(transport, logger);
        transport.Emit(new JObject { ["type"] = "brand_new_frame", ["x"] = 1 });
        transport.EmitText("{oops\n");
        transport.EmitText("[1,2]\n");
        transport.Emit(new JObject { ["noType"] = true });
        Assert.Contains("brand_new_frame", logger.Text("debug"));
        Assert.Contains("Malformed", logger.Text("warn"));
    }

    [Fact]
    public void AnswersHostToolCallsAndHostUriRequestsWithErrors()
    {
        var transport = new MemoryTransport();
        _ = new OmpRpcClient(transport, new MemoryLogger());
        transport.Emit(new JObject { ["type"] = "host_tool_call", ["id"] = "h1", ["toolCallId"] = "t", ["toolName"] = "x", ["arguments"] = new JObject() });
        transport.Emit(new JObject { ["type"] = "host_uri_request", ["id"] = "u1", ["operation"] = "read", ["url"] = "db://x" });
        var toolResult = transport.Sent("host_tool_result")[0];
        Assert.Equal("h1", (string?)toolResult["id"]);
        Assert.True((bool)toolResult["isError"]!);
        Assert.IsType<JArray>(toolResult["result"]!["content"]);
        var uriResult = transport.Sent("host_uri_result")[0];
        Assert.Equal("u1", (string?)uriResult["id"]);
        Assert.True((bool)uriResult["isError"]!);
        Assert.Equal(JTokenType.String, uriResult["error"]!.Type);
    }

    [Fact]
    public void LogsAWarningForResponsesWithoutAnId()
    {
        var logger = new MemoryLogger();
        var transport = new MemoryTransport();
        _ = new OmpRpcClient(transport, logger);
        transport.Emit(new JObject { ["type"] = "response", ["command"] = "parse", ["success"] = false, ["error"] = "Unexpected token" });
        Assert.Contains("Unexpected token", logger.Text("warn"));
    }

    [Fact]
    public async Task ReassemblesChunkedFramesAfterNegotiatingV2()
    {
        var transport = Negotiating();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        transport.Emit(Ready(256));
        await client.StartAsync();
        var output = new List<string>();
        client.CommandOutput += e => output.Add((string)e["text"]!);
        var text = string.Concat(Enumerable.Repeat("żółw ", 60));
        var bytes = Encoding.UTF8.GetBytes(new JObject { ["type"] = "command_output", ["text"] = text }.ToString(Newtonsoft.Json.Formatting.None));
        const int size = 96;
        var count = (bytes.Length + size - 1) / size;
        for (var index = 0; index < count; index++)
        {
            transport.Emit(new JObject
            {
                ["type"] = "rpc_chunk",
                ["chunkId"] = "c1",
                ["index"] = index,
                ["count"] = count,
                ["byteLength"] = bytes.Length,
                ["data"] = Convert.ToBase64String(bytes, index * size, Math.Min(size, bytes.Length - index * size)),
            });
        }
        Assert.Equal(new[] { text }, output);
    }

    [Fact]
    public void TracesAbbreviatedFramesAndRedactsSecretUiResponses()
    {
        var logger = new MemoryLogger(trace: true);
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(new JObject { ["type"] = "command_output", ["text"] = new string('x', 5000) });
        client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = "u1", ["value"] = "hunter2" }, secret: true);
        client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = "u2", ["value"] = "visible" });
        var trace = logger.Text("trace");
        Assert.DoesNotContain(new string('x', 1000), trace);
        Assert.DoesNotContain("hunter2", trace);
        Assert.Contains("visible", trace);
        Assert.Equal("hunter2", (string?)transport.Sent("extension_ui_response")[0]["value"]);
    }

    [Fact]
    public void DoesNotTraceWhenTracingIsDisabled()
    {
        var logger = new MemoryLogger();
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(new JObject { ["type"] = "agent_start" });
        client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = "u", ["confirmed"] = true });
        Assert.Equal("", logger.Text("trace"));
    }

    [Fact]
    public void FollowsTheLoggersTraceSwitchForEveryFrame()
    {
        var logger = new MemoryLogger();
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, logger);
        transport.Emit(new JObject { ["type"] = "agent_start" });
        logger.TraceEnabled = true;
        transport.Emit(new JObject { ["type"] = "agent_end" });
        client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = "u", ["confirmed"] = true });
        logger.TraceEnabled = false;
        transport.Emit(new JObject { ["type"] = "turn_start" });
        var trace = logger.Text("trace");
        Assert.DoesNotContain("agent_start", trace);
        Assert.Contains("agent_end", trace);
        Assert.Contains("extension_ui_response", trace);
        Assert.DoesNotContain("turn_start", trace);
    }

    [Fact]
    public void DescribeCloseNamesASpawnFailureOrTheExitCode()
    {
        Assert.Equal("OMP process failed: cannot start omp", OmpRpcClient.DescribeClose(new TransportClose { Error = new IOException("cannot start omp") }));
        Assert.Equal("OMP process exited (code 3)", OmpRpcClient.DescribeClose(new TransportClose { Code = 3 }));
    }

    [Fact]
    public void DescribeCloseNamesTheProcessIdAndAppendsOmpsLastStderrLines() => Assert.Equal(
            "OMP process 42 exited (code 1)\nOMP stderr (last lines):\nboom\n'node' is not recognized",
            OmpRpcClient.DescribeClose(new TransportClose { Code = 1, Pid = 42, Stderr = "boom\n'node' is not recognized" }));

    [Fact]
    public async Task KeepsStderrOutOfTheErrorsOfRequestsTheCloseRejected()
    {
        var transport = new MemoryTransport();
        var client = new OmpRpcClient(transport, new MemoryLogger());
        var pending = client.RequestAsync("get_state");
        transport.Close(1, pid: 42, stderr: "boom");
        Assert.Equal("OMP process 42 exited (code 1); get_state did not complete", (await Assert.ThrowsAsync<OmpRequestException>(() => pending)).Message);
        Assert.Equal("OMP process 42 exited (code 1); cannot send get_state", (await Assert.ThrowsAsync<OmpRequestException>(() => client.RequestAsync("get_state"))).Message);
    }
}
