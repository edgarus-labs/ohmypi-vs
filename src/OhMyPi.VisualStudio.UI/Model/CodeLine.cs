using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>One line of a code block: its gutter number (empty when none) and its segments in order.</summary>
internal sealed class CodeLine
{
    public CodeLine(string number, IReadOnlyList<CodeSegment> segments)
    {
        Number = number;
        Segments = segments;
    }

    public string Number { get; }

    public IReadOnlyList<CodeSegment> Segments { get; }
}
