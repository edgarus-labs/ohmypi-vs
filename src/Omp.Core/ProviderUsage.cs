using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>A configured provider and its rate limits.</summary>
public sealed class ProviderUsage
{
    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of limits.
    /// </summary>
    public IReadOnlyList<UsageLimit> Limits { get; set; } = Array.Empty<UsageLimit>();
}
