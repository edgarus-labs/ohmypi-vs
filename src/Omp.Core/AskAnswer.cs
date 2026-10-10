using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents the response data for a question, containing the unique identifier, selected options, and any provided custom input.
/// </summary>
public sealed class AskAnswer
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of selected options.
    /// </summary>
    public IReadOnlyList<string> SelectedOptions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the custom input.
    /// </summary>
    public string? CustomInput { get; set; }
}
