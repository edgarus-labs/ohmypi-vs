using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents the available effort options and the currently selected value.
/// </summary>
internal sealed class EffortChoices
{
    /// <summary>
    /// Initializes a new instance of the EffortChoices class with the specified selected value and a list of available effort options.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="options">The collection of options.</param>
    public EffortChoices(string? value, IReadOnlyList<EffortOption> options)
    {
        Value = value;
        Options = options;
    }

    /// <summary>The user's selector (OMP <c>configured</c>), else the effective level.</summary>
    public string? Value { get; }

    /// <summary>
    /// Gets the collection of options.
    /// </summary>
    public IReadOnlyList<EffortOption> Options { get; }
}
