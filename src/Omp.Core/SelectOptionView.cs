namespace Omp.Core;

/// <summary>
/// Represents the data structure for a selectable option, containing a display label and an optional description.
/// </summary>
public sealed class SelectOptionView
{
    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }
}
