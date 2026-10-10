using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>Emitted for every tool execution boundary; consumed by change tracking.</summary>
public sealed class ToolExecutionEvent
{
    /// <summary>
    /// Gets or sets the phase.
    /// </summary>
    public ToolExecutionPhase Phase { get; set; }

    /// <summary>
    /// Gets or sets the tool call id.
    /// </summary>
    public string ToolCallId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the args.
    /// </summary>
    public JToken? Args { get; set; }

    /// <summary>Present on End.</summary>
    public ToolResultView? Result { get; set; }
}
