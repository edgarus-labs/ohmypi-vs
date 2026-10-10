namespace Omp.Core;

/// <summary>
/// Represents the data structure containing token consumption metrics and context window utilization details.
/// </summary>
public sealed class ContextUsageView
{
    /// <summary>
    /// Gets or sets the tokens.
    /// </summary>
    public long Tokens { get; set; }

    /// <summary>
    /// Gets or sets the context window.
    /// </summary>
    public long ContextWindow { get; set; }

    /// <summary>
    /// Gets or sets the percent.
    /// </summary>
    public double Percent { get; set; }
}
