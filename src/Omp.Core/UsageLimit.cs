namespace Omp.Core;

/// <summary>One rate limit of a provider, as OMP's <c>/usage</c> reports it.</summary>
public sealed class UsageLimit
{
    /// <summary>What the limit covers, as "Claude 7 Day" or "5 hours".</summary>
    public string Name { get; set; } = "";

    /// <summary>The account the limit belongs to, as OMP names it.</summary>
    public string Account { get; set; } = "";

    /// <summary>The share of the limit used, 0–100.</summary>
    public double UsedPercent { get; set; }

    /// <summary>When the limit resets, as "in 5d"; null when OMP does not say.</summary>
    public string? Resets { get; set; }

    /// <summary>Whether the current session draws on this limit.</summary>
    public bool InUse { get; set; }
}
