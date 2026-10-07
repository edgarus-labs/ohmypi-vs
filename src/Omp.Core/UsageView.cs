namespace Omp.Core;

public sealed class UsageView
{
    public long Input { get; set; }

    public long Output { get; set; }

    public long CacheRead { get; set; }

    public long CacheWrite { get; set; }

    public double? CostUsd { get; set; }
}
