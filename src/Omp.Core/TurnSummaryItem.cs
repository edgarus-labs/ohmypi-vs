namespace Omp.Core;

/// <summary>Closes an agent turn: how long it took and what its assistant messages cost.</summary>
public sealed class TurnSummaryItem : TranscriptItem
{
    public long DurationMs { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CacheReadTokens { get; set; }

    public long CacheWriteTokens { get; set; }

    /// <summary>Null when OMP reported no pricing for the turn's messages.</summary>
    public double? CostUsd { get; set; }

    public bool Aborted { get; set; }
}
