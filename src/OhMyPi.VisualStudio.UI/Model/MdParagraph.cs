using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Consecutive prose lines; each line keeps its own line break.</summary>
internal sealed class MdParagraph : MdBlock
{
    /// <summary>
    /// Initializes a new instance of the MdParagraph class with the specified collection of lines containing inline elements.
    /// </summary>
    /// <param name="lines">The collection of lines.</param>
    public MdParagraph(IReadOnlyList<IReadOnlyList<MdInline>> lines) => Lines = lines;

    /// <summary>
    /// Gets the collection of lines.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<MdInline>> Lines { get; }
}
