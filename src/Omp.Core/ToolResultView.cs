using Newtonsoft.Json.Linq;

namespace Omp.Core;

public sealed class ToolResultView
{
    public string Text { get; set; } = "";

    public bool IsError { get; set; }

    /// <summary>Raw tool details (tool-specific, e.g. edit's path/diff/oldText/perFileResults).</summary>
    public JToken? Details { get; set; }
}
