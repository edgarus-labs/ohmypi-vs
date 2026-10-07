namespace Omp.Core;

public sealed class PromptImage
{
    /// <summary>Base64-encoded bytes.</summary>
    public string Data { get; set; } = "";

    public string MimeType { get; set; } = "";
}
