namespace Omp.Core;

/// <summary>
/// Represents an image used within a prompt, encapsulating its raw data and associated MIME type.
/// </summary>
public sealed class PromptImage
{
    /// <summary>Base64-encoded bytes.</summary>
    public string Data { get; set; } = "";

    /// <summary>
    /// Gets or sets the mime type.
    /// </summary>
    public string MimeType { get; set; } = "";
}
