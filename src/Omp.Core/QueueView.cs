using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents a read-only view of a queue, containing lists of steering and follow-up identifiers.
/// </summary>
public sealed class QueueView
{
    /// <summary>
    /// Gets or sets the collection of steering.
    /// </summary>
    public IReadOnlyList<string> Steering { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the collection of follow up.
    /// </summary>
    public IReadOnlyList<string> FollowUp { get; set; } = Array.Empty<string>();
}
