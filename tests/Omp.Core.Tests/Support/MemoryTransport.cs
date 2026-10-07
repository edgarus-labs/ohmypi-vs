using System.Text;
using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;

namespace Omp.Core.Tests.Support;

/// <summary>In-memory transport: frames written by the client are parsed and handed to the responder.</summary>
internal class MemoryTransport : IOmpTransport
{
    private readonly Action<JObject, MemoryTransport>? _respond;
    private readonly List<JObject> _written = new();

    public MemoryTransport(Action<JObject, MemoryTransport>? respond = null)
    {
        _respond = respond;
    }

    public event Action<ArraySegment<byte>>? Data;
    public event Action<TransportClose>? Closed;

    public bool IsClosed { get; private set; }
    public bool FailWrites { get; set; }

    /// <summary>Accept writes, then report this error through the write's error callback.</summary>
    public Exception? FailWritesLater { get; set; }

    public IReadOnlyList<JObject> Written
    {
        get { lock (_written) return _written.ToArray(); }
    }

    public void Write(string line, Action<Exception>? onError = null)
    {
        if (FailWrites || IsClosed) throw new IOException("write EPIPE");
        var frame = JObject.Parse(line);
        lock (_written) _written.Add(frame);
        var failure = FailWritesLater;
        if (failure != null)
        {
            Task.Run(() => onError?.Invoke(failure));
            return;
        }
        _respond?.Invoke(frame, this);
    }

    public void Emit(JObject frame) => EmitText(frame.ToString(Newtonsoft.Json.Formatting.None) + "\n");

    public void EmitText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Data?.Invoke(new ArraySegment<byte>(bytes));
    }

    public void Reply(JObject request, JToken? data = null)
    {
        var frame = new JObject { ["type"] = "response", ["id"] = request["id"], ["command"] = request["type"], ["success"] = true };
        if (data != null) frame["data"] = data;
        Emit(frame);
    }

    public void Fail(JObject request, string error, string? code = null)
    {
        var frame = new JObject { ["type"] = "response", ["id"] = request["id"], ["command"] = request["type"], ["success"] = false, ["error"] = error };
        if (code != null) frame["code"] = code;
        Emit(frame);
    }

    public void Close(int? code = 0, int? pid = null, string? stderr = null)
    {
        IsClosed = true;
        Closed?.Invoke(new TransportClose { Code = code, Pid = pid, Stderr = stderr });
    }

    public IReadOnlyList<JObject> Sent(string type) => Written.Where(f => (string?)f["type"] == type).ToArray();

    public static readonly JObject ReadyV2 = new()
    {
        ["type"] = "ready",
        ["protocolVersion"] = 1,
        ["supportedProtocolVersions"] = new JArray(1, 2),
        ["maxFrameBytes"] = 1048576,
        ["maxReassembledFrameBytes"] = 67108864,
    };
}
