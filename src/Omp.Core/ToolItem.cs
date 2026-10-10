using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>
/// Represents a transcript item that encapsulates the execution details, arguments, and outcome of a tool invocation.
/// </summary>
public sealed class ToolItem : TranscriptItem
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the args.
    /// </summary>
    public JToken? Args { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public ToolStatus Status { get; set; }

    /// <summary>Latest streamed partial output, plain text.</summary>
    public string? Partial { get; set; }

    /// <summary>
    /// Gets or sets the result.
    /// </summary>
    public ToolResultView? Result { get; set; }

    /// <summary>Unix milliseconds.</summary>
    public long StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the ended at.
    /// </summary>
    public long? EndedAt { get; set; }
}
