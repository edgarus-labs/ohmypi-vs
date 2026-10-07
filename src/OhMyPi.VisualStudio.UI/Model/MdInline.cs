using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class MdInline
{
    public MdInline(MdInlineKind kind, string text, string? url = null, IReadOnlyList<MdInline>? children = null)
    {
        Kind = kind;
        Text = text;
        Url = url;
        Children = children ?? Array.Empty<MdInline>();
    }

    public MdInlineKind Kind { get; }

    public string Text { get; }

    /// <summary>Spans inside bold or italic text (inline code); empty when the span is plain <see cref="Text"/>.</summary>
    public IReadOnlyList<MdInline> Children { get; }

    /// <summary>Link target as written; the renderer decides whether it is safe to open.</summary>
    public string? Url { get; }
}
