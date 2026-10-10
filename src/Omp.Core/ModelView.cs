using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents the data structure containing detailed configuration and metadata for a specific AI model view, including its provider, capabilities, and cost metrics.
/// </summary>
public sealed class ModelView
{
    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the api.
    /// </summary>
    public string? Api { get; set; }

    /// <summary>
    /// Gets or sets the context window.
    /// </summary>
    public long? ContextWindow { get; set; }

    /// <summary>
    /// Gets or sets the max tokens.
    /// </summary>
    public long? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether reasoning.
    /// </summary>
    public bool Reasoning { get; set; }

    /// <summary>
    /// Gets or sets the collection of thinking efforts.
    /// </summary>
    public IReadOnlyList<string> ThinkingEfforts { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the collection of input.
    /// </summary>
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
