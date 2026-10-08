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
/// What a code block draws, computed without any control: a numbered listing is split into gutter and code (a
/// skipped range becomes one empty line), the code is lexed by language, and search matches cut the tokens into
/// marked and unmarked segments.
/// </summary>
internal static class CodeSegments
{
    /// <param name="mark">Text to mark as matches; null marks nothing.</param>
    public static IReadOnlyList<CodeLine> Build(string text, string? language, Regex? mark)
    {
        var listing = ToolFormat.SplitLineNumbers(text);
        var code = (listing?.Code ?? text).Replace("\r\n", "\n");
        var numbers = new List<string>(listing?.Numbers ?? Array.Empty<string>());
        if (numbers.Any(n => n.IndexOf('-') > 0))
        {
            var codeLines = code.Split('\n');
            for (var i = 0; i < numbers.Count && i < codeLines.Length; i++)
            {
                if (numbers[i].IndexOf('-') > 0)
                {
                    numbers[i] = "";
                    codeLines[i] = "";
                }
            }

            code = string.Join("\n", codeLines);
        }

        var width = 0;
        foreach (var number in numbers)
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
        string Number() => line < numbers.Count ? numbers[line].PadLeft(width) : "";
        foreach (var token in CodeHighlighter.Tokens(code, language))
        {
            var start = token.Start;
            var end = token.Start + token.Length;
            while (start < end)
            {
                var newline = code.IndexOf('\n', start, end - start);
                var stop = newline < 0 ? end : newline;
                AddSegments(segments, code, start, stop, token.Kind, marks);
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

    /// <summary>Adds <paramref name="code"/>[<paramref name="start"/>..<paramref name="end"/>) split where a mark begins or ends.</summary>
    private static void AddSegments(List<CodeSegment> segments, string code, int start, int end, CodeTokenKind kind, List<(int Start, int End)> marks)
    {
        var at = start;
        foreach (var (markStart, markEnd) in marks)
        {
            if (markEnd <= at || markStart >= end)
            {
                continue;
            }

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
