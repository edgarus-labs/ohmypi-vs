using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Omp.Core.Session;

/// <summary>Reduces OMP session events and snapshots into the host's <see cref="SessionView"/> and transcript. Not thread-safe.</summary>
internal sealed class SessionStore
{
    private readonly IOmpLogger _logger;
    private readonly List<TranscriptItem> _items = new List<TranscriptItem>();
    private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, StreamCursor> _cursors = new Dictionary<string, StreamCursor>(StringComparer.Ordinal);
    private string? _lastAssistantId;
    /// <summary>Row of the last OMP user message, which an id-less <c>message_end</c> completes.</summary>
    private string? _lastUserId;
    /// <summary><see cref="Version"/> at the last event that set the queue, the compaction flag and the effort level.</summary>
    private long _queueAt;
    private long _compactingAt;
    private long _thinkingAt;
    private int _localId;
    /// <summary>Row shown for the prompt in flight until OMP's own user message takes it over.</summary>
    private string? _pendingUserId;
    /// <summary>OMP user message ids mapped to the echo rows they took over.</summary>
    private readonly Dictionary<string, string> _userAliases = new Dictionary<string, string>();

    public SessionStore(IOmpLogger logger)
    {
        _logger = logger;
    }

    public event Action<SessionView>? SessionChanged;

    public event Action<TranscriptItem>? ItemChanged;

    public event Action<IReadOnlyList<TranscriptItem>>? Reset;

    public event Action<ToolExecutionEvent>? ToolExecution;

    public SessionView Session { get; private set; } = new SessionView();

    public IReadOnlyList<TranscriptItem> Transcript => _items;

    /// <summary>Name of the most recently started tool that is still running.</summary>
    public string? RunningTool
    {
        get
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] is ToolItem tool && tool.Status == ToolStatus.Running)
                {
                    return tool.Name;
                }
            }

            return null;
        }
    }

    /// <summary>Publish a fresh session snapshot with <paramref name="patch"/> applied.</summary>
    public void Update(Action<SessionView> patch)
    {
        var next = Views.Copy(Session);
        patch(next);
        Session = next;
        Listeners.Raise(SessionChanged, nameof(SessionChanged), next, _logger);
    }

    /// <summary>Counts applied session events; pass it to <see cref="ApplyState"/> as the moment a snapshot was requested.</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Applies a <c>get_state</c> snapshot. With <paramref name="requestedAt"/> (the <see cref="Version"/> when the
    /// snapshot was requested), the queue, the compaction flag and the effort level keep the value of any event applied
    /// after that moment, because the snapshot is older than the event.
    /// </summary>
    public void ApplyState(JObject state, long? requestedAt = null) => Update(v =>
                                                                            {
                                                                                v.SessionId = Json.Str(state, "sessionId");
                                                                                v.SessionFile = Json.Str(state, "sessionFile");
                                                                                v.SessionName = Json.Str(state, "sessionName");
                                                                                v.Model = state["model"] is JObject model ? ModelMapper.ToModelView(model) : null;
                                                                                if (!(_thinkingAt > requestedAt))
                                                                                {
                                                                                    v.ThinkingLevel = Json.Str(state, "thinkingLevel");
                                                                                }

                                                                                v.FastModeEnabled = Json.Bool(state, "fastModeEnabled");
                                                                                v.FastModeActive = Json.Bool(state, "fastModeActive");
                                                                                v.ContextUsage = state["contextUsage"] is JObject usage
                                                                                    ? new ContextUsageView { Tokens = Json.Long(usage, "tokens") ?? 0, ContextWindow = Json.Long(usage, "contextWindow") ?? 0, Percent = Json.Num(usage, "percent") ?? 0 }
                                                                                    : null;
                                                                                v.Todos = (state["todoPhases"] as JArray ?? new JArray()).OfType<JObject>().Select(phase => new TodoPhaseView
                                                                                {
                                                                                    Name = Json.Str(phase, "name") ?? "",
                                                                                    Tasks = (phase["tasks"] as JArray ?? new JArray()).OfType<JObject>()
                                                                                        .Select(task => new TodoTaskView { Content = Json.Str(task, "content") ?? "", Status = Json.Str(task, "status") ?? "pending" })
                                                                                        .ToArray(),
                                                                                }).ToArray();
                                                                                var compacting = Json.Bool(state, "isCompacting");
                                                                                if (compacting is not null && !(_compactingAt > requestedAt))
                                                                                {
                                                                                    v.IsCompacting = compacting.Value;
                                                                                }

                                                                                if (state["queuedMessages"] is JObject queue && !(_queueAt > requestedAt))
                                                                                {
                                                                                    v.Queue = new QueueView { Steering = Json.Strings(queue["steering"]), FollowUp = Json.Strings(queue["followUp"]) };
                                                                                }
                                                                            });

    /// <summary>Replace the transcript with a stored conversation.</summary>
    public void ResetHistory(JArray? messages)
    {
        _items.Clear();
        _items.AddRange(History.ToItems(messages));
        _index.Clear();
        _cursors.Clear();
        _lastAssistantId = null;
        _pendingUserId = null;
        _lastUserId = null;
        _userAliases.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            _index[_items[i].Id] = i;
        }

        Listeners.Raise(Reset, nameof(Reset), (IReadOnlyList<TranscriptItem>)[.. _items], _logger);
        var cost = History.CostOf(messages);
        Update(v => v.CostUsd = cost);
    }

    public void AddNotice(NoticeLevel level, string text) => Add(new NoticeItem { Id = NextLocalId("notice"), Level = level, Text = text });

    /// <summary>
    /// Adds a new command output item containing the specified text to the collection.
    /// </summary>
    /// <param name="text">The text.</param>
    public void AddCommandOutput(string text) => Add(new CommandOutputItem { Id = NextLocalId("output"), Text = text });

    /// <summary>Shows a submitted prompt right away; returns the row id. OMP's next user message replaces its content.</summary>
    public string AddPendingUser(string text, int imageCount)
    {
        var id = NextLocalId("user");
        _pendingUserId = id;
        Add(new UserItem { Id = id, Text = text, ImageCount = imageCount });

        return id;
    }

    /// <summary>Withdraws the echo of a prompt OMP refused, unless OMP already recorded the message.</summary>
    public void RemovePendingUser(string id)
    {
        if (_pendingUserId != id || !_index.TryGetValue(id, out var at))
        {
            return;
        }

        _pendingUserId = null;
        _items.RemoveAt(at);
        _index.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            _index[_items[i].Id] = i;
        }

        Listeners.Raise(Reset, nameof(Reset), (IReadOnlyList<TranscriptItem>)[.. _items], _logger);
    }

    /// <summary>Keeps the echo as the record of a prompt OMP finished without a user message (local slash commands, skills).</summary>
    public void ForgetPendingUser(string id)
    {
        if (_pendingUserId == id)
        {
            _pendingUserId = null;
        }
    }

    /// <summary>
    /// Closes the turn whose items start at <paramref name="from"/>: sums the usage of its assistant messages.
    /// Nothing is added when no assistant replied (local commands).
    /// </summary>
    public void AddTurnSummary(int from, long durationMs, bool aborted)
    {
        var summary = new TurnSummaryItem { Id = NextLocalId("turn"), DurationMs = durationMs, Aborted = aborted };
        var replies = 0;
        for (var i = Math.Max(0, from); i < _items.Count; i++)
        {
            if (!(_items[i] is AssistantItem assistant))
            {
                continue;
            }

            replies++;
            var usage = assistant.Usage;
            if (usage is null)
            {
                continue;
            }

            summary.InputTokens += usage.Input;
            summary.OutputTokens += usage.Output;
            summary.CacheReadTokens += usage.CacheRead;
            summary.CacheWriteTokens += usage.CacheWrite;
            if (usage.CostUsd is not null)
            {
                summary.CostUsd = (summary.CostUsd ?? 0) + usage.CostUsd.Value;
            }
        }
        if (replies > 0)
        {
            Add(summary);
        }
    }

    /// <summary>
    /// Updates the state of the object by processing a JSON event to handle message updates, tool execution lifecycles, and queue or thinking level changes.
    /// </summary>
    /// <param name="e">The e.</param>
    public void ApplyEvent(JObject e)
    {
        Version++;
        switch (Json.Str(e, "type"))
        {
            case "message_start":
                ApplyMessage(Json.Str(e, "messageId"), e["message"] as JObject, true);
                return;

            case "message_end":
                ApplyMessage(Json.Str(e, "messageId"), e["message"] as JObject, false);
                return;

            case "message_update":
                ApplyMessageUpdate(e);
                return;

            case "tool_execution_start":
                ApplyToolStart(Json.Str(e, "toolCallId") ?? "", Json.Str(e, "toolName") ?? "", e["args"]);
                return;

            case "tool_execution_update":
                {
                    var text = PartialText(e["partialResult"]);
                    var hasArgs = e.TryGetValue("args", out var args);
                    PatchTool(Json.Str(e, "toolCallId"), tool =>
                    {
                        if (hasArgs)
                        {
                            tool.Args = args;
                        }

                        if (text is not null)
                        {
                            tool.Partial = text;
                        }
                    });

                    return;
                }
            case "tool_stream_update":
                {
                    var text = PartialText(e["update"]);
                    if (text is not null)
                    {
                        PatchTool(Json.Str(e, "toolCallId"), tool => tool.Partial = text);
                    }

                    return;
                }
            case "tool_execution_end":
                ApplyToolEnd(Json.Str(e, "toolCallId") ?? "", Json.Str(e, "toolName") ?? "", e["result"], Json.Bool(e, "isError"));
                return;

            case "queue_update":
                _queueAt = Version;
                Update(v => v.Queue = new QueueView { Steering = Json.Strings(e["steering"]), FollowUp = Json.Strings(e["followUp"]) });
                return;

            case "thinking_level_changed":
                {
                    var configured = Json.Str(e, "configured");
                    var level = Json.Str(e, "thinkingLevel");
                    var selector = configured ?? level;
                    if (selector is null)
                    {
                        return;
                    }

                    _thinkingAt = Version;
                    Update(v =>
                    {
                        v.ThinkingLevel = level ?? v.ThinkingLevel;
                        v.ThinkingSelector = selector;
                        v.ThinkingResolved = configured == "auto" ? Json.Str(e, "resolved") : null;
                    });

                    return;
                }
            case "notice":
                AddNotice(ParseLevel(Json.Str(e, "level")), Json.Str(e, "message") ?? "");
                return;

            case "auto_compaction_start":
                _compactingAt = Version;
                Update(v => v.IsCompacting = true);
                return;

            case "auto_compaction_end":
                {
                    _compactingAt = Version;
                    Update(v => v.IsCompacting = false);
                    var error = Json.Str(e, "errorMessage");
                    if (!string.IsNullOrEmpty(error))
                    {
                        AddNotice(NoticeLevel.Warning, $"Compaction failed: {error}");
                    }

                    return;
                }
            case "auto_retry_start":
                {
                    var seconds = (long)Math.Floor((Json.Num(e, "delayMs") ?? 0) / 1000 + 0.5);
                    AddNotice(NoticeLevel.Warning, $"Retrying (attempt {Json.Long(e, "attempt")}/{Json.Long(e, "maxAttempts")}) in {seconds}s: {Json.Str(e, "errorMessage")}");

                    return;
                }
            case "auto_retry_end":
                if (Json.Bool(e, "success") != true)
                {
                    AddNotice(NoticeLevel.Error, $"Retry failed: {Json.Str(e, "finalError") ?? "unknown error"}");
                }

                return;
        }
    }

    /// <summary>
    /// Parses a string representation of a notice level into its corresponding NoticeLevel enumeration value, defaulting to NoticeLevel.Info if the input is null or unrecognized.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>The notice level result.</returns>
    internal static NoticeLevel ParseLevel(string? level) =>
        level == "error" ? NoticeLevel.Error : level == "warning" ? NoticeLevel.Warning : NoticeLevel.Info;

    /// <summary>Text carried by a <c>tool_execution_update.partialResult</c> or <c>tool_stream_update.update</c>, if any.</summary>
    private static string? PartialText(JToken? payload)
    {
        var text = Json.Str(payload);
        if (text is not null)
        {
            return text;
        }

        if (payload is JObject record && record["content"] is JArray)
        {
            return Json.TextOf(record["content"]);
        }

        return null;
    }

    /// <summary>
    /// Increments the internal local identifier and returns a formatted string combining the specified prefix with the new ID.
    /// </summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>The string result.</returns>
    private string NextLocalId(string prefix)
    {
        _localId++;

        return $"{prefix}-{_localId}";
    }

    /// <summary>
    /// Adds a transcript item to the internal collection, updates the lookup index, and notifies listeners of the change.
    /// </summary>
    /// <param name="item">The item.</param>
    private void Add(TranscriptItem item)
    {
        _index[item.Id] = _items.Count;
        _items.Add(item);
        Listeners.Raise(ItemChanged, nameof(ItemChanged), item, _logger);
    }

    /// <summary>
    /// Adds the specified transcript item to the collection or updates the existing item and notifies listeners if the item already exists.
    /// </summary>
    /// <param name="item">The item.</param>
    private void Set(TranscriptItem item)
    {
        if (!_index.TryGetValue(item.Id, out var at))
        {
            Add(item);

            return;
        }
        _items[at] = item;
        Listeners.Raise(ItemChanged, nameof(ItemChanged), item, _logger);
    }

    /// <summary>
    /// Retrieves the transcript item associated with the specified identifier, returning null if the item is not found.
    /// </summary>
    /// <param name="id">The unique identifier.</param>
    /// <returns>The transcript item? result.</returns>
    private TranscriptItem? Get(string id) => _index.TryGetValue(id, out var at) ? _items[at] : null;

    /// <summary>
    /// Processes a message object to update the conversation history and usage costs based on the sender&apos;s role and identity.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="message">The message.</param>
    /// <param name="starting">The starting.</param>
    private void ApplyMessage(string? messageId, JObject? message, bool starting)
    {
        if (message is null)
        {
            return;
        }

        var role = Json.Str(message, "role");
        if (role == "user")
        {
            if (Json.Bool(message, "synthetic") == true)
            {
                return;
            }

            string userId;
            if (messageId is not null && _userAliases.TryGetValue(messageId, out var alias))
            {
                userId = alias;
            }
            else if (messageId is null && !starting && _lastUserId is not null)
            {
                userId = _lastUserId;
            }
            else if (_pendingUserId is not null)
            {
                if (messageId is not null)
                {
                    _userAliases[messageId] = _pendingUserId;
                }

                userId = _pendingUserId;
                _pendingUserId = null;
            }
            else
            {
                userId = messageId ?? NextLocalId("user");
            }

            _lastUserId = userId;
            Set(History.UserItem(userId, message));

            return;
        }
        if (role != "assistant")
        {
            return;
        }

        var id = messageId ?? (starting ? NextLocalId("assistant") : _lastAssistantId ?? NextLocalId("assistant"));
        _lastAssistantId = id;
        Set(History.AssistantItem(id, message, starting));
        if (starting)
        {
            _cursors[id] = new StreamCursor();

            return;
        }
        _cursors.Remove(id);
        var cost = Json.Num(Json.Get(Json.Get(message, "usage"), "cost"), "total");
        if (cost is not null)
        {
            Update(v => v.CostUsd = (v.CostUsd ?? 0) + cost.Value);
        }
    }

    /// <summary>
    /// Processes an incoming JSON event to update or append streaming text and thinking deltas to an assistant message in the conversation history.
    /// </summary>
    /// <param name="e">The e.</param>
    private void ApplyMessageUpdate(JObject e)
    {
        var message = e["message"] as JObject;
        if (Json.Str(message, "role") != "assistant")
        {
            return;
        }

        var id = Json.Str(e, "messageId") ?? _lastAssistantId ?? NextLocalId("assistant");
        _lastAssistantId = id;
        if (message!["content"] is JArray)
        {
            Set(History.AssistantItem(id, message, true));

            return;
        }
        var update = e["assistantMessageEvent"] as JObject;
        var type = Json.Str(update, "type");
        if (type != "text_delta" && type != "thinking_delta")
        {
            return;
        }

        var delta = Json.Str(update, "delta") ?? "";
        var contentIndex = (int)(Json.Num(update, "contentIndex") ?? 0);
        var item = Get(id) is AssistantItem existing ? Views.Copy(existing) : new AssistantItem { Id = id, Streaming = true };
        if (!_cursors.TryGetValue(id, out var cursor))
        {
            cursor = new StreamCursor();
            _cursors[id] = cursor;
        }
        if (type == "text_delta")
        {
            if (cursor.TextIndex is not null && cursor.TextIndex != contentIndex && item.Text.Length > 0)
            {
                item.Text += "\n\n";
            }

            cursor.TextIndex = contentIndex;
            item.Text += delta;
        }
        else
        {
            if (cursor.ThinkingIndex is not null && cursor.ThinkingIndex != contentIndex && item.Thinking.Length > 0)
            {
                item.Thinking += "\n\n";
            }

            cursor.ThinkingIndex = contentIndex;
            item.Thinking += delta;
        }
        Set(item);
    }

    /// <summary>
    /// Initializes the tool execution state by recording the start time and raising a tool execution start event.
    /// </summary>
    /// <param name="toolCallId">The unique identifier of the tool call.</param>
    /// <param name="name">The name.</param>
    /// <param name="args">The args.</param>
    private void ApplyToolStart(string toolCallId, string name, JToken? args)
    {
        Set(new ToolItem { Id = toolCallId, Name = name, Args = args, Status = ToolStatus.Running, StartedAt = Listeners.NowMs() });
        Listeners.Raise(ToolExecution, nameof(ToolExecution), new ToolExecutionEvent { Phase = ToolExecutionPhase.Start, ToolCallId = toolCallId, Name = name, Args = args }, _logger);
    }

    /// <summary>
    /// Updates a specific tool item identified by the provided identifier by applying the specified patch action to a copy of the item.
    /// </summary>
    /// <param name="toolCallId">The unique identifier of the tool call.</param>
    /// <param name="patch">The patch.</param>
    private void PatchTool(string? toolCallId, Action<ToolItem> patch)
    {
        if (toolCallId is null || !(Get(toolCallId) is ToolItem existing))
        {
            return;
        }

        var copy = Views.Copy(existing);
        patch(copy);
        Set(copy);
    }

    /// <summary>
    /// Finalizes the execution of a tool call by updating the tool item&apos;s status and result, recording the completion timestamp, and raising a tool execution end event.
    /// </summary>
    /// <param name="toolCallId">The unique identifier of the tool call.</param>
    /// <param name="name">The name.</param>
    /// <param name="rawResult">The raw result.</param>
    /// <param name="isError">The is error.</param>
    private void ApplyToolEnd(string toolCallId, string name, JToken? rawResult, bool? isError)
    {
        var result = History.ToolResultView(rawResult, isError);
        var item = Get(toolCallId) is ToolItem existing
            ? Views.Copy(existing)
            : new ToolItem { Id = toolCallId, Name = name, StartedAt = Listeners.NowMs() };
        item.Status = result.IsError ? ToolStatus.Error : ToolStatus.Done;
        item.Result = result;
        item.EndedAt = Listeners.NowMs();
        item.Partial = null;
        Set(item);
        Listeners.Raise(ToolExecution, nameof(ToolExecution), new ToolExecutionEvent { Phase = ToolExecutionPhase.End, ToolCallId = toolCallId, Name = name, Args = item.Args, Result = result }, _logger);
    }

    /// <summary>
    /// Represents a cursor used to track the current reading positions within the text and thinking streams.
    /// </summary>
    private sealed class StreamCursor
    {
        /// <summary>
        /// Gets or sets the text index.
        /// </summary>
        public int? TextIndex { get; set; }

        /// <summary>
        /// Gets or sets the thinking index.
        /// </summary>
        public int? ThinkingIndex { get; set; }
    }
}
