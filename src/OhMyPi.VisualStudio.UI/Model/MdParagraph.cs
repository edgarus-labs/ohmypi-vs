using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Consecutive prose lines; each line keeps its own line break.</summary>
internal sealed class MdParagraph : MdBlock
{
    public MdParagraph(IReadOnlyList<IReadOnlyList<MdInline>> lines) => Lines = lines;

    public IReadOnlyList<IReadOnlyList<MdInline>> Lines { get; }
}
