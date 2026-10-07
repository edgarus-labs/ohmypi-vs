namespace Omp.Core;

public sealed class InputRequest : InteractionRequest
{
    public string Title { get; set; } = "";

    public string? Placeholder { get; set; }

    /// <summary>Render masked; never echo or log the value.</summary>
    public bool Secret { get; set; }
}
