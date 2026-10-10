namespace Omp.Core;

/// <summary>
/// Represents a request to open an editor interaction, specifying the window title and optional prefilled content.
/// </summary>
public sealed class EditorRequest : InteractionRequest
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the prefill.
    /// </summary>
    public string? Prefill { get; set; }
}
