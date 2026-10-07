using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Omp.Core.Session;

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
            if (!(token is JObject message))
            {
                continue;
            }

            var id = "h" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            switch (Json.Str(message, "role"))
            {
                case "user":
                    if (Json.Bool(message, "synthetic") != true)
                    {
                        items.Add(UserItem(id, message));
                    }

                    break;

                case "assistant":
                    items.Add(AssistantItem(id, message, false));
                    foreach (var block in (message["content"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        if (Json.Str(block, "type") != "toolCall")
                        {
                            continue;
                        }

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
                        if (callId is null || !tools.TryGetValue(callId, out var at))
                        {
                            break;
                        }

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
            if (Json.Str(message, "role") != "assistant")
            {
                continue;
            }

            var cost = Json.Num(Json.Get(Json.Get(message, "usage"), "cost"), "total");
            if (cost is not null)
            {
                total = (total ?? 0) + cost.Value;
            }
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
            if (Json.Str(block, "type") != "thinking")
            {
                continue;
            }

            var text = Json.Str(block, "thinking");
            if (text is not null)
            {
                thinking.Add(text);
            }
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
            if (!string.IsNullOrEmpty(stopReason))
            {
                item.StopReason = stopReason;
            }

            var errorMessage = Json.Str(message, "errorMessage");
            if (!string.IsNullOrEmpty(errorMessage))
            {
                item.ErrorMessage = errorMessage;
            }

            item.Usage = UsageView(message["usage"] as JObject);
        }

        return item;
    }

    public static ToolResultView ToolResultView(JToken? result, bool? isError)
    {
        var record = result as JObject;
        var view = new ToolResultView
        {
            Text = record is not null ? Json.TextOf(record["content"]) : Json.Str(result) ?? "",
            IsError = isError ?? Json.Bool(record, "isError") == true,
        };
        if (record is not null && record.TryGetValue("details", out var details))
        {
            view.Details = details;
        }

        return view;
    }

    private static UsageView? UsageView(JObject? usage)
    {
        if (usage is null)
        {
            return null;
        }

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
