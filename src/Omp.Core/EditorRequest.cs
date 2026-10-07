namespace Omp.Core;

public sealed class EditorRequest : InteractionRequest
{
    public string Title { get; set; } = "";

    public string? Prefill { get; set; }
}
