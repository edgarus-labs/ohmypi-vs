using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Omp.Core.Protocol;

/// <summary>
/// Typed OMP RPC client: handshake, request/response correlation and frame dispatch over an
/// <see cref="IOmpTransport"/>. Frames are dispatched on the transport's reader thread, one at a time.
/// </summary>
internal sealed class OmpRpcClient : IDisposable
{
    private const int TraceStringLimit = 200;
    private const int TraceFrameLimit = 2000;

    /// <summary>Every session event type OMP may emit; types without special handling are still routed as session events.</summary>
    private static readonly HashSet<string> SessionEventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "agent_start", "agent_end", "turn_start", "turn_end", "message_start", "message_update", "message_end",
        "tool_execution_start", "tool_execution_update", "tool_stream_update", "tool_execution_end",
        "auto_compaction_start", "auto_compaction_end", "auto_retry_start", "auto_retry_end",
        "cache_warming_start", "cache_warming_end", "retry_fallback_applied", "retry_fallback_succeeded",
        "model_changed", "config_warnings_changed", "advisor_cost_changed", "advisor_yielded", "ttsr_triggered",
        "todo_reminder", "todo_auto_clear", "irc_message", "notice", "thinking_level_changed", "goal_updated", "queue_update",
    };

    private readonly IOmpTransport _transport;
    private readonly IOmpLogger _logger;
    private readonly int _readyTimeoutMs;
    private readonly int _requestTimeoutMs;
    private readonly object _decodeLock = new object();
    private readonly object _pendingLock = new object();
    private readonly JsonlDecoder _decoder;
    private readonly Dictionary<string, PendingRequest> _pending = new Dictionary<string, PendingRequest>(StringComparer.Ordinal);
    private readonly TaskCompletionSource<JObject> _ready = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
    private ChunkReassembler _reassembler = new ChunkReassembler(ChunkLimits.Default);
    private long _maxFrameBytes;
    private bool _readyReceived;
    private TransportClose? _closed;
    private int _nextId;
    private int _protocolVersion = 1;

    public OmpRpcClient(IOmpTransport transport, IOmpLogger logger, int readyTimeoutMs = 30_000, int requestTimeoutMs = 30_000)
    {
        _transport = transport;
        _logger = logger;
        _readyTimeoutMs = readyTimeoutMs;
        _requestTimeoutMs = requestTimeoutMs;
        _decoder = new JsonlDecoder(Receive, error => _logger.Warn("OMP stdout could not be decoded", error));
        transport.Data += OnData;
        transport.Closed += OnClosed;
    }

    public event Action<JObject>? SessionEvent;

    public event Action<JObject>? PromptResult;

    public event Action? SessionSettled;

    public event Action<JObject>? UiRequest;

    public event Action<JObject>? SubagentFrame;

    public event Action<JObject>? CommandOutput;

    public event Action<JObject>? CommandsUpdate;

    public event Action<JObject>? SessionInfoUpdate;

    public event Action<JObject>? ConfigUpdate;

    /// <summary>OMP asks the client to run one of its host tools; without a handler the call is refused.</summary>
    public event Action<JObject>? HostToolCall;

    /// <summary>OMP no longer needs the result of the host tool call named by the frame's <c>targetId</c>.</summary>
    public event Action<JObject>? HostToolCancel;

    /// <summary>The transport ended; every pending request has already been rejected.</summary>
    public event Action<TransportClose>? Closed;

    public int ProtocolVersion => Volatile.Read(ref _protocolVersion);

    /// <summary>One summary line naming the process, followed by OMP's last stderr lines when it wrote any.</summary>
    public static string DescribeClose(TransportClose close)
    {
        var summary = CloseSummary(close);

        return close.Stderr is not null ? $"{summary}\nOMP stderr (last lines):\n{close.Stderr}" : summary;
    }

    public static string CloseSummary(TransportClose close)
    {
        if (close.Error is not null)
        {
            return $"OMP process failed: {close.Error.Message}";
        }

        var name = close.Pid is null ? "OMP process" : $"OMP process {close.Pid}";

        return close.Code is null ? $"{name} exited" : $"{name} exited (code {close.Code})";
    }

    /// <summary>Wait for the <c>ready</c> frame and negotiate protocol v2 when the server offers it.</summary>
    public async Task StartAsync()
    {
        JObject ready;
        using (var timer = new Timer(_ => _ready.TrySetException(new TimeoutException($"OMP did not send a ready frame within {_readyTimeoutMs} ms")), null, _readyTimeoutMs, Timeout.Infinite))
        {
            ready = await _ready.Task.ConfigureAwait(false);
        }
        if (!(ready["supportedProtocolVersions"] is JArray versions) || !versions.Any(v => Json.Num(v) == 2))
        {
            return;
        }

        try
        {
            var result = await RequestAsync("negotiate_protocol", new JObject { ["protocolVersion"] = 2 }).ConfigureAwait(false);
            Volatile.Write(ref _protocolVersion, Json.Num(result, "protocolVersion") == 2 ? 2 : 1);
        }
        catch (OmpRequestException error) when (error.Code != "closed")
        {
            _logger.Warn("OMP protocol v2 negotiation failed; staying on v1", error);
        }
    }

    public string NextId() => "req_" + Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Send a command and complete with its response <c>data</c> (null when absent).
    /// <paramref name="timeoutMs"/> 0 disables the timeout; <paramref name="id"/> lets the caller correlate later frames.
    /// </summary>
    public Task<JToken?> RequestAsync(string type, JObject? parameters = null, int? timeoutMs = null, string? id = null)
    {
        var requestId = id ?? NextId();
        var frame = parameters is not null ? (JObject)parameters.DeepClone() : new JObject();
        frame["id"] = requestId;
        frame["type"] = type;
        var pending = new PendingRequest(type);
        var timeout = timeoutMs ?? _requestTimeoutMs;
        lock (_pendingLock)
        {
            if (_closed is not null)
            {
                return Task.FromException<JToken?>(new OmpRequestException($"{CloseSummary(_closed)}; cannot send {type}", type, "closed"));
            }

            _pending[requestId] = pending;
        }
        if (timeout > 0)
        {
            pending.Timer = new Timer(_ =>
            {
                if (!Take(requestId, pending))
                {
                    return;
                }

                pending.Completion.TrySetException(new OmpRequestException($"OMP command {type} timed out after {timeout} ms", type, "timeout"));
            }, null, timeout, Timeout.Infinite);
        }
        void WriteFailed(Exception error)
        {
            if (!Take(requestId, pending))
            {
                return;
            }

            pending.Completion.TrySetException(new OmpRequestException($"Failed to send {type} to OMP: {error.Message}", type, "write_failed", error));
        }
        try
        {
            Write(frame, frame, WriteFailed);
        }
        catch (Exception error)
        {
            WriteFailed(error);
        }

        return pending.Completion.Task;
    }

    /// <summary>
    /// Answer an extension UI request. Secret responses are written verbatim but never traced. Throws when the
    /// transport refuses the line; <paramref name="onWriteError"/> (else the log) receives a failure detected after
    /// the transport accepted it.
    /// </summary>
    public void SendUiResponse(JObject response, bool secret = false, Action<Exception>? onWriteError = null)
    {
        var traced = response;
        if (secret && response.ContainsKey("value"))
        {
            traced = (JObject)response.DeepClone();
            traced["value"] = "[redacted]";
        }
        Write(response, traced, onWriteError ?? ReportWriteFailure("extension_ui_response"));
    }

    /// <summary>Completes the host tool call <paramref name="id"/> with <paramref name="result"/>.</summary>
    public void SendHostToolResult(string id, HostToolResult result)
    {
        var frame = new JObject
        {
            ["type"] = "host_tool_result",
            ["id"] = id,
            ["isError"] = result.IsError,
            ["result"] = new JObject
            {
                ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = result.Content }),
                ["isError"] = result.IsError,
            },
        };
        Write(frame, frame, ReportWriteFailure("host_tool_result"));
    }

    public void Dispose()
    {
        _transport.Data -= OnData;
        _transport.Closed -= OnClosed;
        lock (_pendingLock)
        {
            _closed ??= new TransportClose { Error = new ObjectDisposedException("OMP client", "client disposed") };
        }

        _ready.TrySetException(new ObjectDisposedException("OMP client", "OMP client disposed before it was ready"));
        RejectAll("OMP client disposed", "closed");
        SessionEvent = null;
        PromptResult = null;
        SessionSettled = null;
        UiRequest = null;
        SubagentFrame = null;
        CommandOutput = null;
        CommandsUpdate = null;
        SessionInfoUpdate = null;
        ConfigUpdate = null;
        HostToolCall = null;
        HostToolCancel = null;
        Closed = null;
    }

    private bool Take(string id, PendingRequest pending)
    {
        lock (_pendingLock)
        {
            if (!_pending.TryGetValue(id, out var current) || current != pending)
            {
                return false;
            }

            _pending.Remove(id);
        }
        pending.Timer?.Dispose();

        return true;
    }

    private void Write(JObject frame, JObject traced, Action<Exception>? onError)
    {
        var line = Json.Serialize(frame) + "\n";
        if (_logger.TraceEnabled)
        {
            _logger.Trace("out", Abbreviate(traced));
        }

        var limit = Interlocked.Read(ref _maxFrameBytes);
        if (limit > 0)
        {
            var bytes = Encoding.UTF8.GetByteCount(line);
            if (bytes > limit)
            {
                _logger.Warn($"OMP {Json.Str(frame, "type")} frame of {bytes} bytes exceeds the {limit}-byte frame limit OMP advertised; sent as one line");
            }
        }
        _transport.Write(line, onError);
    }

    private Action<Exception> ReportWriteFailure(string type) => error => _logger.Warn($"Failed to send {type} to OMP", error);

    private void OnData(ArraySegment<byte> chunk)
    {
        lock (_decodeLock)
        {
            _decoder.Push(chunk);
        }
    }

    /// <summary>
    /// Processes an incoming JSON token by reassembling it into an OMP frame and dispatching it based on its specified type.
    /// </summary>
    /// <param name="value">The value.</param>
    private void Receive(JToken value)
    {
        var result = _reassembler.Push(value);
        if (result.Error is not null)
        {
            _logger.Warn("OMP frame rejected", result.Error);
        }

        var frame = result.Frame;
        if (frame is null)
        {
            return;
        }

        if (_logger.TraceEnabled)
        {
            _logger.Trace("in", Abbreviate(frame));
        }

        var type = Json.Str(frame, "type");
        if (type is null)
        {
            _logger.Warn($"OMP frame without a type ignored: {Abbreviate(frame)}");

            return;
        }
        try
        {
            Dispatch(type, frame);
        }
        catch (Exception error)
        {
            _logger.Error($"Failed to handle OMP {type} frame", error);
        }
    }

    /// <summary>
    /// Routes incoming frames to their corresponding handler methods or event listeners based on the specified frame type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="frame">The frame.</param>
    private void Dispatch(string type, JObject frame)
    {
        switch (type)
        {
            case "response":
                HandleResponse(frame);
                return;

            case "ready":
                HandleReady(frame);
                return;

            case "prompt_result":
                Listeners.Raise(PromptResult, type, frame, _logger);
                return;

            case "session_settled":
                Listeners.Raise(SessionSettled, type, _logger);
                return;

            case "extension_ui_request":
                Listeners.Raise(UiRequest, type, frame, _logger);
                return;

            case "subagent_lifecycle":
            case "subagent_progress":
            case "subagent_event":
                Listeners.Raise(SubagentFrame, type, frame, _logger);
                return;

            case "command_output":
                Listeners.Raise(CommandOutput, type, frame, _logger);
                return;

            case "available_commands_update":
                Listeners.Raise(CommandsUpdate, type, frame, _logger);
                return;

            case "session_info_update":
                Listeners.Raise(SessionInfoUpdate, type, frame, _logger);
                return;

            case "config_update":
                Listeners.Raise(ConfigUpdate, type, frame, _logger);
                return;

            case "host_tool_call" when HostToolCall is not null:
                Listeners.Raise(HostToolCall, type, frame, _logger);
                return;

            case "host_tool_cancel" when HostToolCancel is not null:
                Listeners.Raise(HostToolCancel, type, frame, _logger);
                return;

            case "host_tool_call":
                {
                    var text = $"Host tool {Json.Str(frame, "toolName")} is not provided by this client.";
                    var result = new JObject
                    {
                        ["type"] = "host_tool_result",
                        ["id"] = frame["id"],
                        ["isError"] = true,
                        ["result"] = new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }) },
                    };
                    Write(result, result, ReportWriteFailure("host_tool_result"));

                    return;
                }
            case "host_uri_request":
                {
                    var result = new JObject
                    {
                        ["type"] = "host_uri_result",
                        ["id"] = frame["id"],
                        ["isError"] = true,
                        ["error"] = $"URL {Json.Str(frame, "url")} is not served by this client.",
                    };
                    Write(result, result, ReportWriteFailure("host_uri_result"));

                    return;
                }
            case "host_tool_cancel":
            case "host_uri_cancel":
                _logger.Debug($"OMP {type} ignored");
                return;

            case "extension_error":
                _logger.Warn($"OMP extension error ({frame["extensionPath"]}, {frame["event"]}): {frame["error"]}");
                return;

            case "rpc_frame_error":
                _logger.Warn($"OMP dropped an oversized {Json.Str(frame, "originalType") ?? "frame"}: {frame["error"]}");
                return;
        }
        if (SessionEventTypes.Contains(type))
        {
            Listeners.Raise(SessionEvent, type, frame, _logger);

            return;
        }
        _logger.Debug($"Unknown OMP frame type {type} ignored");
    }

    /// <summary>
    /// Processes the OMP ready frame to configure frame size limits, initialize the chunk reassembler, and signal the completion of the ready state.
    /// </summary>
    /// <param name="frame">The frame.</param>
    private void HandleReady(JObject frame)
    {
        if (_readyReceived)
        {
            _logger.Debug("Duplicate OMP ready frame ignored");

            return;
        }
        _readyReceived = true;
        var maxFrameBytes = PositiveInteger(frame["maxFrameBytes"]);
        var maxReassembled = PositiveInteger(frame["maxReassembledFrameBytes"]);
        if (maxFrameBytes is not null)
        {
            Interlocked.Exchange(ref _maxFrameBytes, maxFrameBytes.Value);
            _decoder.MaxLineBytes = (int)Math.Min(int.MaxValue, maxFrameBytes.Value);
        }
        _reassembler = new ChunkReassembler(new ChunkLimits(
            maxFrameBytes ?? ChunkLimits.Default.MaxFrameBytes,
            maxReassembled ?? ChunkLimits.Default.MaxReassembledFrameBytes));
        _ready.TrySetResult(frame);
    }

    /// <summary>
    /// Processes an incoming OMP response frame by matching its identifier to a pending request and completing the associated task with either the returned data or a request exception.
    /// </summary>
    /// <param name="frame">The frame.</param>
    private void HandleResponse(JObject frame)
    {
        var command = Json.Str(frame, "command") ?? "unknown";
        var id = Json.Str(frame, "id");
        if (id is null)
        {
            _logger.Warn($"OMP {command} failure without request id: {Json.Str(frame, "error") ?? "unknown error"}");

            return;
        }
        PendingRequest? pending;
        lock (_pendingLock)
        {
            _pending.TryGetValue(id, out pending);
        }

        if (pending is null || !Take(id, pending))
        {
            _logger.Debug($"Late or unknown OMP response {id} ({command}) ignored");

            return;
        }
        if (Json.Bool(frame, "success") == true)
        {
            pending.Completion.TrySetResult(frame["data"]);
        }
        else
        {
            pending.Completion.TrySetException(new OmpRequestException(Json.Str(frame, "error") ?? $"{command} failed", command, Json.Str(frame, "code")));
        }
    }

    /// <summary>
    /// Handles the transport closure by finalizing the decoder, rejecting pending requests, and notifying registered listeners of the closed state.
    /// </summary>
    /// <param name="close">The close.</param>
    private void OnClosed(TransportClose close)
    {
        lock (_pendingLock)
        {
            if (_closed is not null)
            {
                return;
            }

            _closed = close;
        }
        lock (_decodeLock)
        {
            _decoder.End();
        }

        var reason = CloseSummary(close);
        _ready.TrySetException(new OmpRequestException($"{reason} before it was ready", "ready", "closed"));
        RejectAll(reason, "closed");
        Listeners.Raise(Closed, nameof(Closed), close, _logger);
    }

    /// <summary>
    /// Rejects all pending requests by clearing the internal queue, disposing associated timers, and completing each request with an OmpRequestException containing the specified reason and error code.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <param name="code">The code.</param>
    private void RejectAll(string reason, string code)
    {
        List<KeyValuePair<string, PendingRequest>> all;
        lock (_pendingLock)
        {
            all = [.. _pending];
            _pending.Clear();
        }
        foreach (var entry in all)
        {
            entry.Value.Timer?.Dispose();
            entry.Value.Completion.TrySetException(new OmpRequestException($"{reason}; {entry.Value.Command} did not complete", entry.Value.Command, code));
        }
    }

    /// <summary>
    /// Validates whether the provided JSON token is a safe integer greater than zero and returns it as a nullable long, or null if the criteria are not met.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The long? result.</returns>
    private static long? PositiveInteger(JToken? value) =>
        Json.IsSafeInteger(value) && Json.Num(value) > 0 ? (long)Json.Num(value)!.Value : (long?)null;

    /// <summary>
    /// Serializes a JSON token after abbreviating its strings and truncates the resulting string to a predefined trace frame limit if it exceeds the maximum length.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The string result.</returns>
    private static string Abbreviate(JToken frame)
    {
        var json = Json.Serialize(AbbreviateStrings(frame));

        return json.Length > TraceFrameLimit ? json.Substring(0, TraceFrameLimit) + "…" : json;
    }

    /// <summary>
    /// Recursively traverses a JSON token and truncates any string values that exceed the defined trace length limit, appending the original character count to the abbreviated text.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The jtoken result.</returns>
    private static JToken AbbreviateStrings(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var copy = new JObject();
                foreach (var property in obj.Properties())
                {
                    copy[property.Name] = AbbreviateStrings(property.Value);
                }

                return copy;

            case JArray array:
                return new JArray(array.Select(AbbreviateStrings));

            case JValue value when value.Type == JTokenType.String:
                var text = (string)value!;
                return text.Length > TraceStringLimit ? new JValue($"{text.Substring(0, TraceStringLimit)}…({text.Length} chars)") : value;

            default:
                return token;
        }
    }

    /// <summary>
    /// Represents a pending request containing the command string, a task completion source for the JSON response, and an associated timeout timer.
    /// </summary>
    private sealed class PendingRequest
    {
        /// <summary>
        /// Initializes a new instance of the PendingRequest class with the specified command.
        /// </summary>
        /// <param name="command">The command containing the operation data.</param>
        public PendingRequest(string command)
        {
            Command = command;
        }

        /// <summary>
        /// Gets the command.
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// Gets the completion.
        /// </summary>
        public TaskCompletionSource<JToken?> Completion { get; } = new TaskCompletionSource<JToken?>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets or sets the timer.
        /// </summary>
        public Timer? Timer { get; set; }
    }
}
