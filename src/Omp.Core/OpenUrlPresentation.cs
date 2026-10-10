namespace Omp.Core;

/// <summary>
/// Represents a request to open a specific URL with optional accompanying instructions.
/// </summary>
/// <summary>
/// Gets or sets the instructions.
/// <summary>
/// Gets or sets the url.
/// </summary>
/// </summary>
public sealed class OpenUrlPresentation : PresentationRequest { public string Url { get; set; } = ""; public string? Instructions { get; set; } }
