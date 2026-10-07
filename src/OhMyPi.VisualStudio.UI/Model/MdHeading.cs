using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Heading; <see cref="Level"/> is the rendered level: <c>#</c> → 3 … capped at 6, so a heading in a chat answer
/// stays close to body size instead of dwarfing the conversation.
/// </summary>
internal sealed class MdHeading : MdBlock
{
    public MdHeading(int level, IReadOnlyList<MdInline> inlines)
    {
        Level = level;
        Inlines = inlines;
    }

    public int Level { get; }

    public IReadOnlyList<MdInline> Inlines { get; }
}
