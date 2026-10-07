using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;

namespace Omp.Core.Tests.Support;

/// <summary>
/// Scripted OMP process held in memory: it sends <c>ready</c> once started and answers every command with a
/// minimal success unless an override handles it. An override returning <see cref="NoReply"/> sends nothing.
/// </summary>
internal sealed class MemoryOmp : MemoryTransport, IOmpProcessHandle
{
    public static readonly JToken NoReply = new JValue("no reply");
    private static int _nextPid = 900_000;

    private static readonly Dictionary<string, Func<JObject, MemoryOmp, JToken?>> Defaults = new()
    {
        ["negotiate_protocol"] = (f, _) => new JObject { ["protocolVersion"] = f["protocolVersion"] },
        ["set_subagent_subscription"] = (f, _) => new JObject { ["level"] = f["level"] },
        ["set_ask_dialog"] = (f, _) => new JObject { ["enabled"] = f["enabled"] },
        ["set_event_filter"] = (_, _) => new JObject { ["events"] = null, ["messageUpdates"] = "delta" },
        ["get_state"] = (_, _) => new JObject { ["sessionId"] = "memory-session", ["thinkingLevel"] = "off", ["isStreaming"] = false, ["isCompacting"] = false, ["messageCount"] = 0 },
        ["get_available_thinking_levels"] = (_, _) => new JObject { ["levels"] = new JArray("off") },
        ["get_entries"] = (_, _) => new JObject { ["entries"] = new JArray(), ["leafId"] = null },
        ["get_messages_page"] = (_, _) => new JObject { ["messages"] = new JArray(), ["totalMessages"] = 0 },
        ["get_messages"] = (_, _) => new JObject { ["messages"] = new JArray() },
        ["get_subagents"] = (_, _) => new JObject { ["subagents"] = new JArray() },
        ["new_session"] = (_, _) => new JObject { ["cancelled"] = false },
        ["switch_session"] = (_, _) => new JObject { ["cancelled"] = false },
        ["abort"] = (_, _) => null,
    };

    public MemoryOmp(Dictionary<string, Func<JObject, MemoryOmp, JToken?>>? overrides = null)
        : base((frame, transport) => Respond((MemoryOmp)transport, overrides, frame))
    {
        Pid = Interlocked.Increment(ref _nextPid);
    }

    public int? Pid { get; }

    /// <summary>Makes ShutdownAsync fail, as when the process tree survives being killed.</summary>
    public Exception? ShutdownError { get; set; }

    public void Start() => Task.Run(() => Emit(ReadyV2));

    public Task ShutdownAsync(int graceMs)
    {
        if (ShutdownError != null) return Task.FromException(ShutdownError);
        if (!IsClosed) Close(0, Pid);
        return Task.CompletedTask;
    }

    private static void Respond(MemoryOmp omp, Dictionary<string, Func<JObject, MemoryOmp, JToken?>>? overrides, JObject frame)
    {
        var type = (string)frame["type"]!;
        if (type == "extension_ui_response") return;
        Func<JObject, MemoryOmp, JToken?>? handler = null;
        if (overrides?.TryGetValue(type, out handler) != true && !Defaults.TryGetValue(type, out handler))
        {
            omp.Fail(frame, $"Unknown command: {type}");
            return;
        }
        var data = handler!(frame, omp);
        if (!ReferenceEquals(data, NoReply)) omp.Reply(frame, data);
    }
}
