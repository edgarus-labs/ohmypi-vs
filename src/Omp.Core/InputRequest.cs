namespace Omp.Core;

/// <summary>
/// Represents a request for user input, specifying the prompt title, an optional placeholder, and whether the input should be treated as a secret.
/// </summary>
public sealed class InputRequest : InteractionRequest
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the placeholder.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>Render masked; never echo or log the value.</summary>
    public bool Secret { get; set; }
}
