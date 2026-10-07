namespace Omp.Core;

public sealed class ConfirmRequest : InteractionRequest
{
    public string Title { get; set; } = "";

    public string? Message { get; set; }
}
