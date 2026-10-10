using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents an inline element within a Markdown document, containing its type, associated text, optional URL, and a collection of nested child elements.
/// </summary>
internal sealed class MdInline
{
    /// <summary>
    /// Initializes a new instance of the MdInline class with the specified kind, text, optional URL, and optional child elements.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text.</param>
    /// <param name="url">The url.</param>
    /// <param name="children">The collection of children.</param>
    public MdInline(MdInlineKind kind, string text, string? url = null, IReadOnlyList<MdInline>? children = null)
    {
        Kind = kind;
        Text = text;
        Url = url;
        Children = children ?? Array.Empty<MdInline>();
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public MdInlineKind Kind { get; }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }

    /// <summary>Spans inside bold or italic text (inline code); empty when the span is plain <see cref="Text"/>.</summary>
    public IReadOnlyList<MdInline> Children { get; }

    /// <summary>Link target as written; the renderer decides whether it is safe to open.</summary>
    public string? Url { get; }
}
