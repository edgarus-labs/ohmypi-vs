namespace Omp.Core;

/// <summary>
/// Represents a request to present a status message identified by a key and an optional descriptive text.
/// </summary>
/// <summary>
/// Gets or sets the text.
/// </summary>
/// <summary>
/// Gets or sets the key.
/// </summary>
public sealed class StatusPresentation : PresentationRequest { public string Key { get; set; } = ""; public string? Text { get; set; } }
