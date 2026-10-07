using Newtonsoft.Json.Linq;
using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

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
