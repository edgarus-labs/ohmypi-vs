using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Heading; <see cref="Level"/> is the rendered level: <c>#</c> → 3 … capped at 6, so a heading in a chat answer
/// stays close to body size instead of dwarfing the conversation.
/// </summary>
internal sealed class MdHeading : MdBlock
{
    /// <summary>
    /// Initializes a new instance of the MdHeading class with the specified heading level and a collection of inline elements.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="inlines">The collection of inlines.</param>
    public MdHeading(int level, IReadOnlyList<MdInline> inlines)
    {
        Level = level;
        Inlines = inlines;
    }

    /// <summary>
    /// Gets the level.
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// Gets the collection of inlines.
    /// </summary>
    public IReadOnlyList<MdInline> Inlines { get; }
}
