using Newtonsoft.Json.Linq;
using Omp.Core.Protocol;
using System.Text;

namespace Omp.Core.Tests.Support;

/// <summary>In-memory transport: frames written by the client are parsed and handed to the responder.</summary>
internal class MemoryTransport : IOmpTransport
{
    private readonly Action<JObject, MemoryTransport>? _respond;
    private readonly List<JObject> _written = new();

    /// <summary>
    /// Initializes a new instance of the MemoryTransport class with an optional response action.
    /// </summary>
    /// <param name="respond">The respond.</param>
    public MemoryTransport(Action<JObject, MemoryTransport>? respond = null)
    {
        _respond = respond;
    }

    /// <summary>
    /// Occurs when data.
    /// </summary>
    public event Action<ArraySegment<byte>>? Data;

    /// <summary>
    /// Occurs when closed.
    /// </summary>
    public event Action<TransportClose>? Closed;

    /// <summary>
    /// Gets or sets a value indicating whether is closed.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether fail writes.
    /// </summary>
    public bool FailWrites { get; set; }

    /// <summary>Accept writes, then report this error through the write's error callback.</summary>
    public Exception? FailWritesLater { get; set; }

    /// <summary>
    /// Gets the collection of written.
    /// </summary>
    public IReadOnlyList<JObject> Written
    {
        get
        {
            lock (_written)
            {
                return _written.ToArray();
            }
        }
    }

    /// <summary>
    /// Parses a JSON string into a frame, records it in the written collection, and invokes the response handler or an optional error callback based on the current write state.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="onError">The on error.</param>
    /// <exception cref="IOException">Thrown when an error occurs during execution.</exception>
    public void Write(string line, Action<Exception>? onError = null)
    {
        if (FailWrites || IsClosed)
        {
            throw new IOException("write EPIPE");
        }

        var frame = JObject.Parse(line);
        lock (_written)
        {
            _written.Add(frame);
        }

        var failure = FailWritesLater;
        if (failure is not null)
        {
            Task.Run(() => onError?.Invoke(failure));

            return;
        }
        _respond?.Invoke(frame, this);
    }

    /// <summary>
    /// Serializes the specified JSON object to a compact string and emits it as a text frame followed by a newline character.
    /// </summary>
    /// <param name="frame">The frame.</param>
    public void Emit(JObject frame) => EmitText(frame.ToString(Newtonsoft.Json.Formatting.None) + "\n");

    /// <summary>
    /// Encodes the specified text as UTF-8 bytes and invokes the data event to emit the resulting byte segment.
    /// </summary>
    /// <param name="text">The text.</param>
    public void EmitText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Data?.Invoke(new ArraySegment<byte>(bytes));
    }

    /// <summary>
    /// Constructs and emits a response frame based on the provided request identifier and optional data payload.
    /// </summary>
    /// <param name="request">The request containing the operation data.</param>
    /// <param name="data">The data.</param>
    public void Reply(JObject request, JToken? data = null)
    {
        var frame = new JObject { ["type"] = "response", ["id"] = request["id"], ["command"] = request["type"], ["success"] = true };
        if (data is not null)
        {
            frame["data"] = data;
        }

        Emit(frame);
    }

    /// <summary>
    /// Constructs and emits a failure response frame associated with the specified request, including an error message and an optional error code.
    /// </summary>
    /// <param name="request">The request containing the operation data.</param>
    /// <param name="error">The error.</param>
    /// <param name="code">The code.</param>
    public void Fail(JObject request, string error, string? code = null)
    {
        var frame = new JObject { ["type"] = "response", ["id"] = request["id"], ["command"] = request["type"], ["success"] = false, ["error"] = error };
        if (code is not null)
        {
            frame["code"] = code;
        }

        Emit(frame);
    }

    /// <summary>
    /// Closes the transport connection and invokes the Closed event with the specified exit code, process identifier, and standard error output.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="pid">The unique identifier of the p.</param>
    /// <param name="stderr">The stderr.</param>
    public void Close(int? code = 0, int? pid = null, string? stderr = null)
    {
        IsClosed = true;
        Closed?.Invoke(new TransportClose { Code = code, Pid = pid, Stderr = stderr });
    }

    /// <summary>
    /// Retrieves a collection of sent objects that match the specified type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>A collection of iread only list items.</returns>
    public IReadOnlyList<JObject> Sent(string type) => Written.Where(f => (string?)f["type"] == type).ToArray();

    /// <summary>
    /// The ready v2.
    /// </summary>
    public static readonly JObject ReadyV2 = new()
    {
        ["type"] = "ready",
        ["protocolVersion"] = 1,
        ["supportedProtocolVersions"] = new JArray(1, 2),
        ["maxFrameBytes"] = 1048576,
        ["maxReassembledFrameBytes"] = 67108864,
    };
}
