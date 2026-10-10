using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a single item within a Markdown list, containing its raw text, parsed inline elements, and any nested child lists.
/// </summary>
internal sealed class MdListItem
{
    /// <summary>
    /// Gets or sets the raw.
    /// </summary>
    public string Raw { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection of inlines.
    /// </summary>
    public IReadOnlyList<MdInline> Inlines { get; set; } = Array.Empty<MdInline>();

    /// <summary>
    /// Gets the collection of children.
    /// </summary>
    public List<MdList> Children { get; } = new List<MdList>();
}
