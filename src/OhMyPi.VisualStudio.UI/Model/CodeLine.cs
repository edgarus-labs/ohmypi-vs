using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>One line of a code block: its gutter number (empty when none) and its segments in order.</summary>
internal sealed class CodeLine
{
    /// <summary>
    /// Initializes a new instance of the CodeLine class with the specified line number and a collection of code segments.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="segments">The collection of segments.</param>
    public CodeLine(string number, IReadOnlyList<CodeSegment> segments)
    {
        Number = number;
        Segments = segments;
    }

    /// <summary>
    /// Gets the number.
    /// </summary>
    public string Number { get; }

    /// <summary>
    /// Gets the collection of segments.
    /// </summary>
    public IReadOnlyList<CodeSegment> Segments { get; }
}
