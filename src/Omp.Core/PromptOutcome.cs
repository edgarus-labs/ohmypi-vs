namespace Omp.Core;

/// <summary>
/// Represents the result of a prompt operation, including its execution status, potential error details, and admission state.
/// </summary>
public sealed class PromptOutcome
{
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public PromptStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether session settled.
    /// </summary>
    public bool SessionSettled { get; set; }

    /// <summary>
    /// OMP acknowledged the prompt, so its user message is part of the session even when the outcome is an error.
    /// False only when OMP refused the prompt before admitting it (or it could not be sent); the caller may offer
    /// the text again.
    /// </summary>
    public bool Admitted { get; set; }
}
