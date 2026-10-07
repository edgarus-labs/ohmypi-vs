using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class ModelView
{
    public string Provider { get; set; } = "";

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Api { get; set; }

    public long? ContextWindow { get; set; }

    public long? MaxTokens { get; set; }

    public bool Reasoning { get; set; }

    public IReadOnlyList<string> ThinkingEfforts { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Input { get; set; } = Array.Empty<string>();

    /// <summary>USD per million tokens, when OMP reports pricing.</summary>
    public ModelCostView? Cost { get; set; }

    /// <summary>The model's vendor class as OMP reports it (anthropic, openai, …); null when unknown.</summary>
    public string? VendorClass { get; set; }

    /// <summary>The family within the class (opus, sonnet, gpt, …); null when unknown.</summary>
    public string? Family { get; set; }

    /// <summary>The model's version within its family, as "5.5.0"; null when unknown.</summary>
    public string? Revision { get; set; }
}
