namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Something attached to the next prompt; shown as a removable chip above the input.</summary>
internal abstract class Attachment
{
    /// <summary>
    /// Initializes a new instance of the Attachment class with the specified label.
    /// </summary>
    /// <param name="label">The label.</param>
    protected Attachment(string label) => Label = label;

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label { get; }
}
