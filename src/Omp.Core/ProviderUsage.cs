using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>A configured provider and its rate limits.</summary>
public sealed class ProviderUsage
{
    public string Provider { get; set; } = "";

    public IReadOnlyList<UsageLimit> Limits { get; set; } = Array.Empty<UsageLimit>();
}
