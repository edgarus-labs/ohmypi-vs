using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>A slash command OMP accepts as a prompt (OMP <c>available_commands_update</c>).</summary>
public sealed class SlashCommandView
{
    /// <summary>Name without the leading slash.</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Argument hint, e.g. "&lt;plan|scan&gt;".</summary>
    public string? Hint { get; set; }

    public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();

    /// <summary>builtin, skill, extension, ... as reported by OMP.</summary>
    public string? Source { get; set; }
}
