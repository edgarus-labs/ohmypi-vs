using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents the data structure for a question view, including its header, options, and selection constraints.
/// </summary>
public sealed class AskQuestionView
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the question.
    /// </summary>
    public string Question { get; set; } = "";

    /// <summary>
    /// Gets or sets the header.
    /// </summary>
    public string? Header { get; set; }

    /// <summary>
    /// Gets or sets the collection of options.
    /// </summary>
    public IReadOnlyList<AskOptionView> Options { get; set; } = Array.Empty<AskOptionView>();

    /// <summary>
    /// Gets or sets a value indicating whether multi.
    /// </summary>
    public bool Multi { get; set; }

    /// <summary>
    /// Gets or sets the recommended.
    /// </summary>
    public int? Recommended { get; set; }
}
