using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>Emitted for every tool execution boundary; consumed by change tracking.</summary>
public sealed class ToolExecutionEvent
{
    public ToolExecutionPhase Phase { get; set; }

    public string ToolCallId { get; set; } = "";

    public string Name { get; set; } = "";

    public JToken? Args { get; set; }

    /// <summary>Present on End.</summary>
    public ToolResultView? Result { get; set; }
}
