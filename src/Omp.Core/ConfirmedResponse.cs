namespace Omp.Core;

/// <summary>
/// Represents a response to an interaction that indicates whether the action was confirmed.
/// </summary>
/// <summary>
/// Gets or sets a value indicating whether confirmed.
/// </summary>
public sealed class ConfirmedResponse : InteractionResponse { public bool Confirmed { get; set; } }
