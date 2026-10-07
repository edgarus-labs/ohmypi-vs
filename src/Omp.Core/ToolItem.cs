using Newtonsoft.Json.Linq;

namespace Omp.Core;

public sealed class ToolItem : TranscriptItem
{
    public string Name { get; set; } = "";

    public JToken? Args { get; set; }

    public ToolStatus Status { get; set; }

    /// <summary>Latest streamed partial output, plain text.</summary>
    public string? Partial { get; set; }

    public ToolResultView? Result { get; set; }

    /// <summary>Unix milliseconds.</summary>
    public long StartedAt { get; set; }

    public long? EndedAt { get; set; }
}
