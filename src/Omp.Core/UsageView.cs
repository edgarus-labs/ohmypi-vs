namespace Omp.Core;

/// <summary>
/// Represents a read-only view of resource usage metrics, including data transfer, cache operations, and associated costs.
/// </summary>
public sealed class UsageView
{
    /// <summary>
    /// Gets or sets the input.
    /// </summary>
    public long Input { get; set; }

    /// <summary>
    /// Gets or sets the output.
    /// </summary>
    public long Output { get; set; }

    /// <summary>
    /// Gets or sets the cache read.
    /// </summary>
    public long CacheRead { get; set; }

    /// <summary>
    /// Gets or sets the cache write.
    /// </summary>
    public long CacheWrite { get; set; }

    /// <summary>
    /// Gets or sets the cost usd.
    /// </summary>
    public double? CostUsd { get; set; }
}
