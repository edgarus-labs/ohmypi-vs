using System;
using System.Collections.Generic;

namespace Omp.Core.Changes;

/// <summary>Where tool paths resolve (<see cref="Cwd"/>) and which directories may be tracked (<see cref="Roots"/>).</summary>
public sealed class ChangeScope
{
    /// <summary>
    /// Gets or sets the cwd.
    /// </summary>
    public string Cwd { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of roots.
    /// </summary>
    public IReadOnlyList<string> Roots { get; set; } = Array.Empty<string>();
}
