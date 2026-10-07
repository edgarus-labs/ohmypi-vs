namespace Omp.Core;

public sealed class PromptOutcome
{
    public PromptStatus Status { get; set; }

    public string? Error { get; set; }

    public bool SessionSettled { get; set; }

    /// <summary>
    /// OMP acknowledged the prompt, so its user message is part of the session even when the outcome is an error.
    /// False only when OMP refused the prompt before admitting it (or it could not be sent); the caller may offer
    /// the text again.
    /// </summary>
    public bool Admitted { get; set; }
}
