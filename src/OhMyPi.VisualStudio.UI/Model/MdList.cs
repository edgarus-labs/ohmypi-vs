using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a Markdown list block containing a collection of list items and configuration for ordering and starting index.
/// </summary>
internal sealed class MdList : MdBlock
{
    /// <summary>
    /// Initializes a new instance of the MdList class with the specified ordering preference and starting index.
    /// </summary>
    /// <param name="ordered">The ordered.</param>
    /// <param name="start">The start.</param>
    public MdList(bool ordered, int start)
    {
        Ordered = ordered;
        Start = start;
    }

    /// <summary>
    /// Gets a value indicating whether ordered.
    /// </summary>
    public bool Ordered { get; }

    /// <summary>
    /// Gets the start.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the collection of items.
    /// </summary>
    public List<MdListItem> Items { get; } = new List<MdListItem>();
}
