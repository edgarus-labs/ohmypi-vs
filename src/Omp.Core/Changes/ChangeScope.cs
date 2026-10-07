using System;
using System.Collections.Generic;

namespace Omp.Core.Changes;

/// <summary>Where tool paths resolve (<see cref="Cwd"/>) and which directories may be tracked (<see cref="Roots"/>).</summary>
public sealed class ChangeScope
{
    public string Cwd { get; set; } = "";

    public IReadOnlyList<string> Roots { get; set; } = Array.Empty<string>();
}
