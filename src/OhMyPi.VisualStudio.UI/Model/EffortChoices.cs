using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class EffortChoices
{
    public EffortChoices(string? value, IReadOnlyList<EffortOption> options)
    {
        Value = value;
        Options = options;
    }

    /// <summary>The user's selector (OMP <c>configured</c>), else the effective level.</summary>
    public string? Value { get; }

    public IReadOnlyList<EffortOption> Options { get; }
}
