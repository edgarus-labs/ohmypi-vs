namespace Omp.Core;

/// <summary>Closes an agent turn: how long it took and what its assistant messages cost.</summary>
public sealed class TurnSummaryItem : TranscriptItem
{
    /// <summary>
    /// Gets or sets the duration ms.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the input tokens.
    /// </summary>
    public long InputTokens { get; set; }

    /// <summary>
    /// Gets or sets the output tokens.
    /// </summary>
    public long OutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the cache read tokens.
    /// </summary>
    public long CacheReadTokens { get; set; }

    /// <summary>
    /// Gets or sets the cache write tokens.
    /// </summary>
    public long CacheWriteTokens { get; set; }

    /// <summary>Null when OMP reported no pricing for the turn's messages.</summary>
    public double? CostUsd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether aborted.
    /// </summary>
    public bool Aborted { get; set; }
}
