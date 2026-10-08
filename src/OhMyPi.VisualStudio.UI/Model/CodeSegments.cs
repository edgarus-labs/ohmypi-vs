using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A run of one line of code: its text, token kind and whether it is a search match.</summary>
internal readonly struct CodeSegment
{
    public CodeSegment(string text, CodeTokenKind kind, bool marked)
    {
        Text = text;
        Kind = kind;
        Marked = marked;
    }

    public string Text { get; }

    public CodeTokenKind Kind { get; }

    public bool Marked { get; }
}

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

/// <summary>
/// What a code block draws, computed without any control: the gutter numbers of a numbered listing (a skipped
/// range becomes one empty line), the code lexed by language, and search matches cutting the tokens into marked
/// and unmarked segments.
/// </summary>
internal static class CodeSegments
{
    /// <param name="code">The code to draw, without line-number prefixes.</param>
    /// <param name="numbers">The gutter number of each line of <paramref name="code"/> (see <see cref="ToolFormat.SplitLineNumbers"/>); null for code without a gutter.</param>
    /// <param name="mark">Text to mark as matches; null marks nothing.</param>
    public static IReadOnlyList<CodeLine> Build(string code, IReadOnlyList<string>? numbers, string? language, Regex? mark)
    {
        code = code.Replace("\r\n", "\n");
        var gutter = new List<string>(numbers ?? Array.Empty<string>());
        if (gutter.Any(n => n.IndexOf('-') > 0))
        {
            var codeLines = code.Split('\n');
            for (var i = 0; i < gutter.Count && i < codeLines.Length; i++)
            {
                if (gutter[i].IndexOf('-') > 0)
                {
                    gutter[i] = "";
                    codeLines[i] = "";
                }
            }

            code = string.Join("\n", codeLines);
        }

        var width = 0;
        foreach (var number in gutter)
        {
            width = Math.Max(width, number.Length);
        }

        var marks = new List<(int Start, int End)>();
        if (mark is not null)
        {
            try
            {
                foreach (Match match in mark.Matches(code))
                {
                    if (match.Length > 0)
                    {
                        marks.Add((match.Index, match.Index + match.Length));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                marks.Clear();
            }
        }

        var lines = new List<CodeLine>();
        var segments = new List<CodeSegment>();
        var line = 0;
        var nextMark = 0;
        string Number() => line < gutter.Count ? gutter[line].PadLeft(width) : "";
        foreach (var token in CodeHighlighter.Tokens(code, language))
        {
            var start = token.Start;
            var end = token.Start + token.Length;
            while (start < end)
            {
                var newline = code.IndexOf('\n', start, end - start);
                var stop = newline < 0 ? end : newline;
                AddSegments(segments, code, start, stop, token.Kind, marks, ref nextMark);
                if (newline < 0)
                {
                    break;
                }

                lines.Add(new CodeLine(Number(), segments));
                segments = new List<CodeSegment>();
                line++;
                start = newline + 1;
            }
        }

        lines.Add(new CodeLine(Number(), segments));

        return lines;
    }

    /// <summary>
    /// Adds <paramref name="code"/>[<paramref name="start"/>..<paramref name="end"/>) split where a mark begins or
    /// ends. Pieces come in text order, so <paramref name="nextMark"/> (the first mark that may still reach a later
    /// piece) only moves forward.
    /// </summary>
    private static void AddSegments(List<CodeSegment> segments, string code, int start, int end, CodeTokenKind kind, List<(int Start, int End)> marks, ref int nextMark)
    {
        while (nextMark < marks.Count && marks[nextMark].End <= start)
        {
            nextMark++;
        }

        var at = start;
        for (var i = nextMark; i < marks.Count && marks[i].Start < end; i++)
        {
            var (markStart, markEnd) = marks[i];
            if (markStart > at)
            {
                segments.Add(new CodeSegment(code.Substring(at, markStart - at), kind, false));
                at = markStart;
            }

            var stop = Math.Min(markEnd, end);
            segments.Add(new CodeSegment(code.Substring(at, stop - at), kind, true));
            at = stop;
        }

        if (at < end)
        {
            segments.Add(new CodeSegment(code.Substring(at, end - at), kind, false));
        }
    }
}
