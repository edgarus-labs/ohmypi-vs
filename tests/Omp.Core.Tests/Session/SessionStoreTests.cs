using Newtonsoft.Json.Linq;
using Omp.Core.Session;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Session;

public class SessionStoreTests
{
    internal static JObject Usage(double total) => new()
    {
        ["input"] = 10, ["output"] = 5, ["cacheRead"] = 1, ["cacheWrite"] = 2, ["totalTokens"] = 18,
        ["cost"] = new JObject { ["input"] = 0, ["output"] = 0, ["cacheRead"] = 0, ["cacheWrite"] = 0, ["total"] = total },
    };

    internal static JObject Assistant(JArray content, Action<JObject>? extra = null)
    {
        var message = new JObject
        {
            ["role"] = "assistant", ["content"] = content, ["api"] = "a", ["provider"] = "p", ["model"] = "m1",
            ["usage"] = Usage(0.01), ["stopReason"] = "stop", ["timestamp"] = 1,
        };
        extra?.Invoke(message);
        return message;
    }

    private static JObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    private static JObject Delta(string id, string type, int index, string delta) => new()
    {
        ["type"] = "message_update", ["messageId"] = id, ["message"] = new JObject { ["role"] = "assistant" },
        ["assistantMessageEvent"] = new JObject { ["type"] = type, ["contentIndex"] = index, ["delta"] = delta },
    };

    private readonly SessionStore _s = new(new MemoryLogger());
    private readonly List<TranscriptItem> _changes = new();
    private readonly List<ToolExecutionEvent> _tools = new();

    public SessionStoreTests()
    {
        _s.ItemChanged += item => _changes.Add(item);
        _s.ToolExecution += e => _tools.Add(e);
    }

    private void Apply(JObject e) => _s.ApplyEvent(e);

    [Fact]
    public void StreamsAssistantTextAndThinkingDeltasKeyedByMessageId()
    {
        Apply(new JObject { ["type"] = "message_start", ["messageId"] = "m-1", ["message"] = Assistant(new JArray()) });
        Apply(Delta("m-1", "thinking_delta", 0, "hmm"));
        Apply(Delta("m-1", "text_delta", 1, "Hel"));
        Apply(Delta("m-1", "text_delta", 1, "lo"));
        var item = Assert.IsType<AssistantItem>(Assert.Single(_s.Transcript));
        Assert.Equal(("m-1", "Hello", "hmm", true, "m1"), (item.Id, item.Text, item.Thinking, item.Streaming, item.Model));
        Apply(Delta("m-1", "text_delta", 3, "Again"));
        Assert.Equal("Hello\n\nAgain", ((AssistantItem)_s.Transcript[0]).Text);
        Assert.NotSame(_changes[_changes.Count - 1], _changes[_changes.Count - 2]);
    }

    [Fact]
    public void ReplacesStreamedContentWithTheFullMessageOnMessageEndAndSumsCost()
    {
        Apply(new JObject { ["type"] = "message_start", ["messageId"] = "m-1", ["message"] = Assistant(new JArray()) });
        Apply(Delta("m-1", "text_delta", 0, "Hel"));
        Apply(new JObject
        {
            ["type"] = "message_end", ["messageId"] = "m-1",
            ["message"] = Assistant(new JArray(
                new JObject { ["type"] = "thinking", ["thinking"] = "plan" },
                Text("Hello world"),
                new JObject { ["type"] = "toolCall", ["id"] = "t1", ["name"] = "read", ["arguments"] = new JObject() })),
        });
        var item = Assert.IsType<AssistantItem>(_s.Transcript[0]);
        Assert.Equal("Hello world", item.Text);
        Assert.Equal("plan", item.Thinking);
        Assert.False(item.Streaming);
        Assert.Equal("stop", item.StopReason);
        Assert.Equal((10L, 5L, 1L, 2L, (double?)0.01), (item.Usage!.Input, item.Usage.Output, item.Usage.CacheRead, item.Usage.CacheWrite, item.Usage.CostUsd));
        Assert.Equal(0.01, _s.Session.CostUsd);
        Apply(new JObject { ["type"] = "message_end", ["messageId"] = "m-2", ["message"] = Assistant(new JArray(Text("x")), m => m["usage"] = Usage(0.02)) });
        Assert.Equal(0.03, _s.Session.CostUsd!.Value, 10);
    }

    [Fact]
    public void RebuildsFromTheFullPartialMessageWhenUpdatesAreNotProjectedToDeltas()
    {
        Apply(new JObject
        {
            ["type"] = "message_update", ["messageId"] = "m-9", ["message"] = Assistant(new JArray(Text("Full so far"))),
            ["assistantMessageEvent"] = new JObject { ["type"] = "text_delta", ["contentIndex"] = 0, ["delta"] = "far" },
        });
        Assert.Equal("Full so far", ((AssistantItem)_s.Transcript[0]).Text);
    }

    [Fact]
    public void RecordsUserMessagesAndSkipsSyntheticOnes()
    {
        Apply(new JObject { ["type"] = "message_start", ["messageId"] = "u-1", ["message"] = new JObject { ["role"] = "user", ["content"] = "hi", ["timestamp"] = 1 } });
        Apply(new JObject
        {
            ["type"] = "message_end", ["messageId"] = "u-1",
            ["message"] = new JObject { ["role"] = "user", ["content"] = new JArray(Text("hi"), new JObject { ["type"] = "image", ["data"] = "", ["mimeType"] = "image/png" }), ["timestamp"] = 1 },
        });
        Apply(new JObject { ["type"] = "message_start", ["messageId"] = "u-2", ["message"] = new JObject { ["role"] = "user", ["content"] = "reminder", ["timestamp"] = 2, ["synthetic"] = true } });
        var user = Assert.IsType<UserItem>(Assert.Single(_s.Transcript));
        Assert.Equal(("u-1", "hi", 1), (user.Id, user.Text, user.ImageCount));
    }

    [Fact]
    public void TracksToolExecutionsWithPartialOutputResultsAndDetails()
    {
        Apply(new JObject { ["type"] = "tool_execution_start", ["toolCallId"] = "t1", ["toolName"] = "edit", ["args"] = new JObject { ["path"] = "a.ts" } });
        Assert.Equal(ToolStatus.Running, ((ToolItem)_s.Transcript[0]).Status);
        Apply(new JObject { ["type"] = "tool_execution_update", ["toolCallId"] = "t1", ["toolName"] = "edit", ["partialResult"] = new JObject { ["content"] = new JArray(Text("working")) } });
        Assert.Equal("working", ((ToolItem)_s.Transcript[0]).Partial);
        Apply(new JObject { ["type"] = "tool_stream_update", ["toolCallId"] = "t1", ["toolName"] = "edit", ["update"] = "streamed" });
        Assert.Equal("streamed", ((ToolItem)_s.Transcript[0]).Partial);
        Apply(new JObject { ["type"] = "tool_stream_update", ["toolCallId"] = "t1", ["toolName"] = "edit", ["update"] = new JObject { ["files"] = new JArray() } });
        Assert.Equal("streamed", ((ToolItem)_s.Transcript[0]).Partial);
        var details = new JObject { ["path"] = "a.ts", ["oldText"] = "x", ["newText"] = "y", ["diff"] = "-x\n+y" };
        Apply(new JObject
        {
            ["type"] = "tool_execution_end", ["toolCallId"] = "t1", ["toolName"] = "edit",
            ["result"] = new JObject { ["content"] = new JArray(Text("Edited a.ts"), Text("ok")), ["details"] = details }, ["isError"] = false,
        });
        var done = Assert.IsType<ToolItem>(_s.Transcript[0]);
        Assert.Equal(ToolStatus.Done, done.Status);
        Assert.Equal("Edited a.ts\nok", done.Result!.Text);
        Assert.False(done.Result.IsError);
        Assert.True(JToken.DeepEquals(details, done.Result.Details));
        Assert.Null(done.Partial);
        Assert.True(done.EndedAt >= done.StartedAt);
        Assert.Equal(new[] { "Start t1 edit", "End t1 edit" }, _tools.Select(t => $"{t.Phase} {t.ToolCallId} {t.Name}"));
        Assert.True(JToken.DeepEquals(new JObject { ["path"] = "a.ts" }, _tools[0].Args));
        Assert.True(JToken.DeepEquals(details, _tools[1].Result!.Details));
        Assert.True(JToken.DeepEquals(new JObject { ["path"] = "a.ts" }, _tools[1].Args));
        Assert.Null(_s.RunningTool);
    }

    [Fact]
    public void MarksFailedToolsAsErrorAndReportsTheRunningTool()
    {
        Apply(new JObject { ["type"] = "tool_execution_start", ["toolCallId"] = "t1", ["toolName"] = "bash", ["args"] = new JObject() });
        Assert.Equal("bash", _s.RunningTool);
        Apply(new JObject { ["type"] = "tool_execution_end", ["toolCallId"] = "t1", ["toolName"] = "bash", ["result"] = new JObject { ["content"] = new JArray(Text("exit 1")) }, ["isError"] = true });
        Assert.Equal(ToolStatus.Error, ((ToolItem)_s.Transcript[0]).Status);
    }

    [Fact]
    public void KeepsTheAutoSelectorApartFromTheEffortItResolvesTo()
    {
        Apply(new JObject { ["type"] = "thinking_level_changed", ["thinkingLevel"] = "high", ["configured"] = "auto" });
        Assert.Equal(("auto", "high", (string?)null), (_s.Session.ThinkingSelector, _s.Session.ThinkingLevel, _s.Session.ThinkingResolved));
        Apply(new JObject { ["type"] = "thinking_level_changed", ["thinkingLevel"] = "low", ["configured"] = "auto", ["resolved"] = "low" });
        Assert.Equal(("auto", "low", (string?)"low"), (_s.Session.ThinkingSelector, _s.Session.ThinkingLevel, _s.Session.ThinkingResolved));
        Apply(new JObject { ["type"] = "thinking_level_changed", ["thinkingLevel"] = "medium" });
        Assert.Equal(("medium", "medium", (string?)null), (_s.Session.ThinkingSelector, _s.Session.ThinkingLevel, _s.Session.ThinkingResolved));
    }

    [Fact]
    public void AppliesQueueThinkingLevelCompactionNoticesAndRetries()
    {
        Apply(new JObject { ["type"] = "queue_update", ["steering"] = new JArray("a"), ["followUp"] = new JArray("b") });
        Assert.Equal(new[] { "a" }, _s.Session.Queue.Steering);
        Assert.Equal(new[] { "b" }, _s.Session.Queue.FollowUp);
        Apply(new JObject { ["type"] = "thinking_level_changed", ["thinkingLevel"] = "high" });
        Assert.Equal("high", _s.Session.ThinkingLevel);
        Apply(new JObject { ["type"] = "auto_compaction_start", ["reason"] = "threshold", ["action"] = "context-full" });
        Assert.True(_s.Session.IsCompacting);
        Apply(new JObject { ["type"] = "auto_compaction_end", ["action"] = "context-full", ["aborted"] = false, ["willRetry"] = false });
        Assert.False(_s.Session.IsCompacting);
        Apply(new JObject { ["type"] = "notice", ["level"] = "warning", ["message"] = "careful" });
        Apply(new JObject { ["type"] = "auto_retry_start", ["attempt"] = 1, ["maxAttempts"] = 3, ["delayMs"] = 2500, ["errorMessage"] = "overloaded" });
        Apply(new JObject { ["type"] = "auto_retry_end", ["success"] = false, ["attempt"] = 3, ["finalError"] = "gave up" });
        Assert.Equal(new[]
        {
            "Warning: careful",
            "Warning: Retrying (attempt 1/3) in 3s: overloaded",
            "Error: Retry failed: gave up",
        }, _s.Transcript.OfType<NoticeItem>().Select(n => $"{n.Level}: {n.Text}"));
    }

    [Fact]
    public void KeepsQueueCompactionAndEffortFromEventsNewerThanAStateSnapshot()
    {
        var state = new JObject
        {
            ["sessionId"] = "s1", ["thinkingLevel"] = "low", ["isCompacting"] = false,
            ["queuedMessages"] = new JObject { ["steering"] = new JArray(), ["followUp"] = new JArray() },
        };
        var requested = _s.Version;
        Apply(new JObject { ["type"] = "queue_update", ["steering"] = new JArray("newer"), ["followUp"] = new JArray() });
        Apply(new JObject { ["type"] = "auto_compaction_start", ["reason"] = "threshold", ["action"] = "context-full" });
        Apply(new JObject { ["type"] = "thinking_level_changed", ["thinkingLevel"] = "high" });
        _s.ApplyState(state, requested);
        Assert.Equal(("s1", "newer", true, "high"), (_s.Session.SessionId, _s.Session.Queue.Steering.Single(), _s.Session.IsCompacting, _s.Session.ThinkingLevel));
        _s.ApplyState(state, _s.Version);
        Assert.Equal((0, false, "low"), (_s.Session.Queue.Steering.Count, _s.Session.IsCompacting, _s.Session.ThinkingLevel));
    }

    [Fact]
    public void ShowsOneRowForAUserMessageWithoutAMessageId()
    {
        JObject UserEvent(string type, string text) => new() { ["type"] = type, ["message"] = new JObject { ["role"] = "user", ["content"] = text, ["timestamp"] = 1 } };
        Apply(UserEvent("message_start", "first"));
        Apply(UserEvent("message_end", "first"));
        Assert.Equal(new[] { "first" }, _s.Transcript.OfType<UserItem>().Select(u => u.Text));
        Apply(UserEvent("message_start", "second"));
        Apply(UserEvent("message_end", "second"));
        Assert.Equal(new[] { "first", "second" }, _s.Transcript.OfType<UserItem>().Select(u => u.Text));
    }

    [Fact]
    public void MapsGetStateIntoTheSessionView()
    {
        _s.ApplyState(new JObject
        {
            ["sessionId"] = "s1", ["sessionFile"] = "/x/s1.jsonl", ["sessionName"] = "Name", ["thinkingLevel"] = "low",
            ["fastModeEnabled"] = true, ["fastModeActive"] = false, ["isStreaming"] = false, ["isCompacting"] = false,
            ["queuedMessages"] = new JObject { ["steering"] = new JArray(), ["followUp"] = new JArray("later") },
            ["todoPhases"] = new JArray(new JObject { ["name"] = "Todos", ["tasks"] = new JArray(new JObject { ["content"] = "do it", ["status"] = "in_progress" }) }),
            ["contextUsage"] = new JObject { ["tokens"] = 1100, ["contextWindow"] = 200000, ["percent"] = 0.55 },
            ["model"] = new JObject
            {
                ["id"] = "m", ["name"] = "M", ["api"] = "x", ["provider"] = "p", ["baseUrl"] = "", ["reasoning"] = false, ["input"] = new JArray("text"),
                ["cost"] = new JObject { ["input"] = 1, ["output"] = 2, ["cacheRead"] = 0, ["cacheWrite"] = 0 }, ["contextWindow"] = 1000, ["maxTokens"] = 100,
            },
        });
        var v = _s.Session;
        Assert.Equal(("s1", "/x/s1.jsonl", "Name", "m", "low"), (v.SessionId, v.SessionFile, v.SessionName, v.Model!.Id, v.ThinkingLevel));
        Assert.True(v.FastModeEnabled);
        Assert.False(v.FastModeActive);
        Assert.Empty(v.Queue.Steering);
        Assert.Equal(new[] { "later" }, v.Queue.FollowUp);
        var phase = Assert.Single(v.Todos);
        Assert.Equal("Todos", phase.Name);
        Assert.Equal(("do it", "in_progress"), (phase.Tasks[0].Content, phase.Tasks[0].Status));
        Assert.Equal((1100L, 200000L, 0.55), (v.ContextUsage!.Tokens, v.ContextUsage.ContextWindow, v.ContextUsage.Percent));
    }

    [Fact]
    public void PublishesAFreshSessionSnapshotForEveryChange()
    {
        var before = _s.Session;
        _s.Update(v => v.SessionName = "x");
        Assert.NotSame(before, _s.Session);
        Assert.Null(before.SessionName);
        Assert.Equal("x", _s.Session.SessionName);
    }

    [Fact]
    public void AddsCommandOutputAndNoticeItems()
    {
        _s.AddCommandOutput("result");
        _s.AddNotice(NoticeLevel.Error, "bad");
        Assert.IsType<CommandOutputItem>(_s.Transcript[0]);
        Assert.IsType<NoticeItem>(_s.Transcript[1]);
        Assert.NotEqual(_s.Transcript[0].Id, _s.Transcript[1].Id);
    }

    [Fact]
    public void ResetsTheTranscriptAndCostFromHistory()
    {
        IReadOnlyList<TranscriptItem>? reset = null;
        _s.Reset += items => reset = items;
        _s.ResetHistory(new JArray(new JObject { ["role"] = "user", ["content"] = "x", ["timestamp"] = 1 }, Assistant(new JArray(Text("y")))));
        Assert.Equal(2, reset!.Count);
        Assert.Equal(0.01, _s.Session.CostUsd);
    }
}

public class HistoryTests
{
    private static JObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    [Fact]
    public void MapsUserAssistantToolCallsWithResultsAndBashExecutions()
    {
        var messages = new JArray(
            new JObject { ["role"] = "user", ["content"] = "fix it", ["timestamp"] = 1 },
            SessionStoreTests.Assistant(new JArray(
                Text("Reading"),
                new JObject { ["type"] = "toolCall", ["id"] = "t1", ["name"] = "read", ["arguments"] = new JObject { ["path"] = "a" } },
                new JObject { ["type"] = "toolCall", ["id"] = "t2", ["name"] = "edit", ["arguments"] = new JObject { ["path"] = "a" } })),
            new JObject { ["role"] = "toolResult", ["toolCallId"] = "t1", ["toolName"] = "read", ["content"] = new JArray(Text("contents")), ["isError"] = false, ["timestamp"] = 2 },
            new JObject { ["role"] = "toolResult", ["toolCallId"] = "t2", ["toolName"] = "edit", ["content"] = new JArray(Text("nope")), ["isError"] = true, ["timestamp"] = 3, ["details"] = new JObject { ["path"] = "a" } },
            new JObject { ["role"] = "developer", ["timestamp"] = 4 },
            new JObject { ["role"] = "bashExecution", ["command"] = "ls", ["output"] = "a\nb", ["cancelled"] = false, ["truncated"] = false, ["timestamp"] = 5 });
        var items = History.ToItems(messages);
        Assert.Equal(new[] { typeof(UserItem), typeof(AssistantItem), typeof(ToolItem), typeof(ToolItem), typeof(CommandOutputItem) }, items.Select(i => i.GetType()));
        Assert.Equal("Reading", ((AssistantItem)items[1]).Text);
        var t1 = (ToolItem)items[2];
        Assert.Equal((ToolStatus.Done, "contents", 2L), (t1.Status, t1.Result!.Text, t1.EndedAt!.Value));
        var t2 = (ToolItem)items[3];
        Assert.Equal(ToolStatus.Error, t2.Status);
        Assert.True(t2.Result!.IsError);
        Assert.True(JToken.DeepEquals(new JObject { ["path"] = "a" }, t2.Result.Details));
        Assert.Equal("$ ls\na\nb", ((CommandOutputItem)items[4]).Text);
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void MarksAStoredToolCallWithoutAResultAsAnErrorWithNoResultRecorded()
    {
        var items = History.ToItems(new JArray(SessionStoreTests.Assistant(
            new JArray(new JObject { ["type"] = "toolCall", ["id"] = "t1", ["name"] = "bash", ["arguments"] = new JObject { ["command"] = "sleep 100" } }),
            m => m["timestamp"] = 7)));
        var tool = Assert.IsType<ToolItem>(items[1]);
        Assert.Equal(("t1", "bash", ToolStatus.Error, "No result recorded", 7L), (tool.Id, tool.Name, tool.Status, tool.Result!.Text, tool.StartedAt));
    }
}
