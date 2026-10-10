namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a selectable effort option containing a display label and its corresponding value.
/// </summary>
internal sealed class EffortOption
{
    /// <summary>
    /// Initializes a new instance of the EffortOption class with the specified value and label.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="label">The label.</param>
    public EffortOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>
    /// Gets the value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label { get; }
}
