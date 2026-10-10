using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents a request to prompt a user for a selection by providing a title and a list of available options.
/// </summary>
public sealed class SelectRequest : InteractionRequest
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of options.
    /// </summary>
    public IReadOnlyList<SelectOptionView> Options { get; set; } = Array.Empty<SelectOptionView>();
}
