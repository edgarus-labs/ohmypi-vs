namespace Omp.Core;

/// <summary>
/// Represents the data structure for displaying an ask option, including its label, description, and preview content.
/// </summary>
public sealed class AskOptionView
{
    /// <summary>
    /// Gets or sets the label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the preview.
    /// </summary>
    public string? Preview { get; set; }
}
