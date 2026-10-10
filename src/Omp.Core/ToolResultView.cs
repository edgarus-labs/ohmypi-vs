using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>
/// Represents the data structure containing the outcome of a tool execution, including the result text, error status, and associated metadata.
/// </summary>
public sealed class ToolResultView
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets a value indicating whether is error.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>Raw tool details (tool-specific, e.g. edit's path/diff/oldText/perFileResults).</summary>
    public JToken? Details { get; set; }
}
