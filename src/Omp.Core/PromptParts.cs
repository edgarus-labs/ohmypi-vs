using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>The parts of a user prompt.</summary>
public sealed class PromptParts
{
    /// <summary>
    /// Complete <c>&lt;editor-context&gt;</c> block. <see cref="PromptFormatter.SplitUserMessage"/> reads it from prompts
    /// other OMP clients composed; <see cref="PromptFormatter.Format"/> writes it back only so that a split prompt
    /// formats to the same text. This extension never composes one.
    /// </summary>
    public string? EditorContext { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of pasted.
    /// </summary>
    public IReadOnlyList<PastedText> Pasted { get; set; } = Array.Empty<PastedText>();

    /// <summary>File paths as they should appear in the mention (relative to the working directory when inside it).</summary>
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
}
