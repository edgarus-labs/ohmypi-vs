namespace Omp.Core;

public abstract class InteractionRequest
{
    public string Id { get; set; } = "";

    public int? TimeoutMs { get; set; }
}
