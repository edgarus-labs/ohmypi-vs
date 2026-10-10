namespace Omp.Core;

/// <summary>
/// Represents a presentation request for displaying text within an editor.
/// </summary>
/// <summary>
/// Gets or sets the text.
/// </summary>
public sealed class EditorTextPresentation : PresentationRequest { public string Text { get; set; } = ""; }
