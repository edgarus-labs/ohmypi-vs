using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Session
{
    /// <summary>Maps stored conversations (<c>get_messages</c>, <c>get_subagent_messages</c>) into transcript items.</summary>
    internal static class History
    {
        /// <summary>Tool calls become tool items completed by their results.</summary>
        public static IReadOnlyList<TranscriptItem> ToItems(JArray? messages)
        {
            var items = new List<TranscriptItem>();
            var tools = new Dictionary<string, int>(StringComparer.Ordinal);
            var index = -1;
            foreach (var token in messages ?? new JArray())
            {
                index++;
                if (!(token is JObject message)) continue;
                var id = "h" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                switch (Json.Str(message, "role"))
                {
                    case "user":
                        if (Json.Bool(message, "synthetic") != true) items.Add(UserItem(id, message));
                        break;
                    case "assistant":
                        items.Add(AssistantItem(id, message, false));
                        foreach (var block in (message["content"] as JArray ?? new JArray()).OfType<JObject>())
                        {
                            if (Json.Str(block, "type") != "toolCall") continue;
                            var callId = Json.Str(block, "id") ?? "";
                            tools[callId] = items.Count;
                            items.Add(new ToolItem
                            {
                                Id = callId,
                                Name = Json.Str(block, "name") ?? "",
                                Args = block["arguments"],
                                Status = ToolStatus.Error,
                                Result = new ToolResultView { Text = "No result recorded", IsError = true },
                                StartedAt = Json.Long(message, "timestamp") ?? 0,
                            });
                        }
                        break;
                    case "toolResult":
                    {
                        var view = ToolResultView(message, Json.Bool(message, "isError"));
                        var callId = Json.Str(message, "toolCallId");
                        if (callId == null || !tools.TryGetValue(callId, out var at)) break;
                        var tool = Views.Copy((ToolItem)items[at]);
                        tool.Status = view.IsError ? ToolStatus.Error : ToolStatus.Done;
                        tool.Result = view;
                        tool.EndedAt = Json.Long(message, "timestamp");
                        items[at] = tool;
                        break;
                    }
                    case "bashExecution":
                        items.Add(new CommandOutputItem { Id = id, Text = $"$ {Json.Str(message, "command")}\n{Json.Str(message, "output")}" });
                        break;
                }
            }
            return items;
        }

        public static double? CostOf(JArray? messages)
        {
            double? total = null;
            foreach (var message in (messages ?? new JArray()).OfType<JObject>())
            {
                if (Json.Str(message, "role") != "assistant") continue;
                var cost = Json.Num(Json.Get(Json.Get(message, "usage"), "cost"), "total");
                if (cost != null) total = (total ?? 0) + cost.Value;
            }
            return total;
        }

        public static UserItem UserItem(string id, JObject message)
        {
            var content = message["content"];
            var images = content is JArray blocks ? blocks.Count(b => Json.Str(b, "type") == "image") : 0;
            return new UserItem { Id = id, Text = Json.TextOf(content), ImageCount = images };
        }

        public static AssistantItem AssistantItem(string id, JObject message, bool streaming)
        {
            var content = message["content"];
            var thinking = new List<string>();
            foreach (var block in (content as JArray ?? new JArray()).OfType<JObject>())
            {
                if (Json.Str(block, "type") != "thinking") continue;
                var text = Json.Str(block, "thinking");
                if (text != null) thinking.Add(text);
            }
            var model = Json.Str(message, "model");
            var item = new AssistantItem
            {
                Id = id,
                Text = Json.TextOf(content, "\n\n"),
                Thinking = string.Join("\n\n", thinking),
                Streaming = streaming,
                Model = string.IsNullOrEmpty(model) ? null : model,
            };
            if (!streaming)
            {
                var stopReason = Json.Str(message, "stopReason");
                if (!string.IsNullOrEmpty(stopReason)) item.StopReason = stopReason;
                var errorMessage = Json.Str(message, "errorMessage");
                if (!string.IsNullOrEmpty(errorMessage)) item.ErrorMessage = errorMessage;
                item.Usage = UsageView(message["usage"] as JObject);
            }
            return item;
        }

        public static ToolResultView ToolResultView(JToken? result, bool? isError)
        {
            var record = result as JObject;
            var view = new ToolResultView
            {
                Text = record != null ? Json.TextOf(record["content"]) : Json.Str(result) ?? "",
                IsError = isError ?? Json.Bool(record, "isError") == true,
            };
            if (record != null && record.TryGetValue("details", out var details)) view.Details = details;
            return view;
        }

        private static UsageView? UsageView(JObject? usage)
        {
            if (usage == null) return null;
            return new UsageView
            {
                Input = Json.Long(usage, "input") ?? 0,
                Output = Json.Long(usage, "output") ?? 0,
                CacheRead = Json.Long(usage, "cacheRead") ?? 0,
                CacheWrite = Json.Long(usage, "cacheWrite") ?? 0,
                CostUsd = Json.Num(Json.Get(usage, "cost"), "total"),
            };
        }
    }

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
                    if (_items[i] is ToolItem tool && tool.Status == ToolStatus.Running) return tool.Name;
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
        public void ApplyState(JObject state, long? requestedAt = null)
        {
            Update(v =>
            {
                v.SessionId = Json.Str(state, "sessionId");
                v.SessionFile = Json.Str(state, "sessionFile");
                v.SessionName = Json.Str(state, "sessionName");
                v.Model = state["model"] is JObject model ? ModelMapper.ToModelView(model) : null;
                if (!(_thinkingAt > requestedAt)) v.ThinkingLevel = Json.Str(state, "thinkingLevel");
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
                if (compacting != null && !(_compactingAt > requestedAt)) v.IsCompacting = compacting.Value;
                if (state["queuedMessages"] is JObject queue && !(_queueAt > requestedAt))
                    v.Queue = new QueueView { Steering = Json.Strings(queue["steering"]), FollowUp = Json.Strings(queue["followUp"]) };
            });
        }

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
            for (var i = 0; i < _items.Count; i++) _index[_items[i].Id] = i;
            Listeners.Raise(Reset, nameof(Reset), (IReadOnlyList<TranscriptItem>)_items.ToArray(), _logger);
            var cost = History.CostOf(messages);
            Update(v => v.CostUsd = cost);
        }

        public void AddNotice(NoticeLevel level, string text) => Add(new NoticeItem { Id = NextLocalId("notice"), Level = level, Text = text });

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
            if (_pendingUserId != id || !_index.TryGetValue(id, out var at)) return;
            _pendingUserId = null;
            _items.RemoveAt(at);
            _index.Clear();
            for (var i = 0; i < _items.Count; i++) _index[_items[i].Id] = i;
            Listeners.Raise(Reset, nameof(Reset), (IReadOnlyList<TranscriptItem>)_items.ToArray(), _logger);
        }

        /// <summary>Keeps the echo as the record of a prompt OMP finished without a user message (local slash commands, skills).</summary>
        public void ForgetPendingUser(string id)
        {
            if (_pendingUserId == id) _pendingUserId = null;
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
                if (!(_items[i] is AssistantItem assistant)) continue;
                replies++;
                var usage = assistant.Usage;
                if (usage == null) continue;
                summary.InputTokens += usage.Input;
                summary.OutputTokens += usage.Output;
                summary.CacheReadTokens += usage.CacheRead;
                summary.CacheWriteTokens += usage.CacheWrite;
                if (usage.CostUsd != null) summary.CostUsd = (summary.CostUsd ?? 0) + usage.CostUsd.Value;
            }
            if (replies > 0) Add(summary);
        }

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
                        if (hasArgs) tool.Args = args;
                        if (text != null) tool.Partial = text;
                    });
                    return;
                }
                case "tool_stream_update":
                {
                    var text = PartialText(e["update"]);
                    if (text != null) PatchTool(Json.Str(e, "toolCallId"), tool => tool.Partial = text);
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
                    if (selector == null) return;
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
                    if (!string.IsNullOrEmpty(error)) AddNotice(NoticeLevel.Warning, $"Compaction failed: {error}");
                    return;
                }
                case "auto_retry_start":
                {
                    var seconds = (long)Math.Floor((Json.Num(e, "delayMs") ?? 0) / 1000 + 0.5);
                    AddNotice(NoticeLevel.Warning, $"Retrying (attempt {Json.Long(e, "attempt")}/{Json.Long(e, "maxAttempts")}) in {seconds}s: {Json.Str(e, "errorMessage")}");
                    return;
                }
                case "auto_retry_end":
                    if (Json.Bool(e, "success") != true) AddNotice(NoticeLevel.Error, $"Retry failed: {Json.Str(e, "finalError") ?? "unknown error"}");
                    return;
            }
        }

        internal static NoticeLevel ParseLevel(string? level) =>
            level == "error" ? NoticeLevel.Error : level == "warning" ? NoticeLevel.Warning : NoticeLevel.Info;

        /// <summary>Text carried by a <c>tool_execution_update.partialResult</c> or <c>tool_stream_update.update</c>, if any.</summary>
        private static string? PartialText(JToken? payload)
        {
            var text = Json.Str(payload);
            if (text != null) return text;
            if (payload is JObject record && record["content"] is JArray) return Json.TextOf(record["content"]);
            return null;
        }

        private string NextLocalId(string prefix)
        {
            _localId++;
            return $"{prefix}-{_localId}";
        }

        private void Add(TranscriptItem item)
        {
            _index[item.Id] = _items.Count;
            _items.Add(item);
            Listeners.Raise(ItemChanged, nameof(ItemChanged), item, _logger);
        }

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

        private TranscriptItem? Get(string id) => _index.TryGetValue(id, out var at) ? _items[at] : null;

        private void ApplyMessage(string? messageId, JObject? message, bool starting)
        {
            if (message == null) return;
            var role = Json.Str(message, "role");
            if (role == "user")
            {
                if (Json.Bool(message, "synthetic") == true) return;
                string userId;
                if (messageId != null && _userAliases.TryGetValue(messageId, out var alias)) userId = alias;
                else if (messageId == null && !starting && _lastUserId != null) userId = _lastUserId;
                else if (_pendingUserId != null)
                {
                    if (messageId != null) _userAliases[messageId] = _pendingUserId;
                    userId = _pendingUserId;
                    _pendingUserId = null;
                }
                else userId = messageId ?? NextLocalId("user");
                _lastUserId = userId;
                Set(History.UserItem(userId, message));
                return;
            }
            if (role != "assistant") return;
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
            if (cost != null) Update(v => v.CostUsd = (v.CostUsd ?? 0) + cost.Value);
        }

        private void ApplyMessageUpdate(JObject e)
        {
            var message = e["message"] as JObject;
            if (Json.Str(message, "role") != "assistant") return;
            var id = Json.Str(e, "messageId") ?? _lastAssistantId ?? NextLocalId("assistant");
            _lastAssistantId = id;
            if (message!["content"] is JArray)
            {
                Set(History.AssistantItem(id, message, true));
                return;
            }
            var update = e["assistantMessageEvent"] as JObject;
            var type = Json.Str(update, "type");
            if (type != "text_delta" && type != "thinking_delta") return;
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
                if (cursor.TextIndex != null && cursor.TextIndex != contentIndex && item.Text.Length > 0) item.Text += "\n\n";
                cursor.TextIndex = contentIndex;
                item.Text += delta;
            }
            else
            {
                if (cursor.ThinkingIndex != null && cursor.ThinkingIndex != contentIndex && item.Thinking.Length > 0) item.Thinking += "\n\n";
                cursor.ThinkingIndex = contentIndex;
                item.Thinking += delta;
            }
            Set(item);
        }

        private void ApplyToolStart(string toolCallId, string name, JToken? args)
        {
            Set(new ToolItem { Id = toolCallId, Name = name, Args = args, Status = ToolStatus.Running, StartedAt = Listeners.NowMs() });
            Listeners.Raise(ToolExecution, nameof(ToolExecution), new ToolExecutionEvent { Phase = ToolExecutionPhase.Start, ToolCallId = toolCallId, Name = name, Args = args }, _logger);
        }

        private void PatchTool(string? toolCallId, Action<ToolItem> patch)
        {
            if (toolCallId == null || !(Get(toolCallId) is ToolItem existing)) return;
            var copy = Views.Copy(existing);
            patch(copy);
            Set(copy);
        }

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

        private sealed class StreamCursor
        {
            public int? TextIndex { get; set; }
            public int? ThinkingIndex { get; set; }
        }
    }
}
