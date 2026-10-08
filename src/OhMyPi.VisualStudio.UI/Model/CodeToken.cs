using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A run of <see cref="Length"/> characters of code starting at <see cref="Start"/>, all of one <see cref="Kind"/>.</summary>
internal readonly struct CodeToken
{
    public CodeToken(int start, int length, CodeTokenKind kind)
    {
        Start = start;
        Length = length;
        Kind = kind;
    }

    public int Start { get; }

    public int Length { get; }

    public CodeTokenKind Kind { get; }
}
