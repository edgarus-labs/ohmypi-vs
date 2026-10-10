namespace Omp.Core;

/// <summary>
/// Represents a request to prompt the user for confirmation, containing a title and an optional descriptive message.
/// </summary>
public sealed class ConfirmRequest : InteractionRequest
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string? Message { get; set; }
}
