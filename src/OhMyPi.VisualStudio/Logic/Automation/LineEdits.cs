using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation
{
    /// <summary>A replacement of a 0-based text span of a line-based buffer.</summary>
    internal sealed class LineEdit
    {
        public LineEdit(int startLine, int startIndex, int endLine, int endIndex, string text)
        {
            StartLine = startLine;
            StartIndex = startIndex;
            EndLine = endLine;
            EndIndex = endIndex;
            Text = text;
        }

        public int StartLine { get; }
        public int StartIndex { get; }
        public int EndLine { get; }
        public int EndIndex { get; }
        public string Text { get; }
    }

    /// <summary>Line arithmetic for reading and replacing whole lines of an editor buffer.</summary>
    internal static class LineEdits
    {
        /// <summary>Splits on CRLF, LF and CR. A trailing line break yields a final empty line, as an editor buffer has.</summary>
        public static IReadOnlyList<string> SplitLines(string text)
        {
            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '\r' && c != '\n') continue;
                lines.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                start = i + 1;
            }
            lines.Add(text.Substring(start));
            return lines;
        }

        /// <summary>The line break that ends <paramref name="firstLine"/> (a first line including its break); CRLF when there is none.</summary>
        public static string DetectLineBreak(string? firstLine)
        {
            if (firstLine == null) return "\r\n";
            if (firstLine.EndsWith("\r\n", StringComparison.Ordinal)) return "\r\n";
            if (firstLine.EndsWith("\n", StringComparison.Ordinal)) return "\n";
            if (firstLine.EndsWith("\r", StringComparison.Ordinal)) return "\r";
            return "\r\n";
        }

        public static string NormalizeLineBreaks(string text) =>
            text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>The 1-based inclusive range to read; a missing end is the last line and an end past it is clamped.</summary>
        /// <exception cref="InvalidOperationException">The range is empty or starts after the last line.</exception>
        public static void ResolveRange(int totalLines, int? startLine, int? endLine, out int first, out int last)
        {
            first = startLine ?? 1;
            last = endLine ?? totalLines;
            if (first < 1) throw new InvalidOperationException($"startLine must be at least 1, got {first}.");
            if (first > totalLines) throw new InvalidOperationException($"The document has {totalLines} lines; startLine {first} is past the end.");
            if (last > totalLines) last = totalLines;
            if (last < first) throw new InvalidOperationException($"endLine {last} is before startLine {first}.");
        }

        /// <summary>
        /// The buffer edit that replaces lines <paramref name="startLine"/>..<paramref name="endLine"/> (1-based, inclusive;
        /// endLine = startLine - 1 inserts before startLine, startLine = totalLines + 1 appends) with <paramref name="text"/>.
        /// Empty text deletes the lines; otherwise one trailing line break of the text is dropped and its lines, joined with
        /// <paramref name="lineBreak"/>, take the place of the whole lines.
        /// </summary>
        /// <param name="lengthOfLine">Length of a 0-based line without its line break.</param>
        /// <exception cref="InvalidOperationException">The range does not fit the buffer.</exception>
        public static LineEdit Plan(int totalLines, Func<int, int> lengthOfLine, int startLine, int endLine, string text, string lineBreak)
        {
            if (startLine < 1) throw new InvalidOperationException($"startLine must be at least 1, got {startLine}.");
            if (endLine < startLine - 1) throw new InvalidOperationException($"endLine {endLine} must be at least startLine - 1 ({startLine - 1}).");
            if (startLine > totalLines + 1) throw new InvalidOperationException($"The document has {totalLines} lines; startLine {startLine} is past the end (use {totalLines + 1} to append).");
            if (endLine > totalLines) throw new InvalidOperationException($"The document has {totalLines} lines; endLine {endLine} is past the end.");

            var replacement = new List<string>(text.Length == 0 ? Array.Empty<string>() : SplitLines(text));
            if (replacement.Count > 1 && replacement[replacement.Count - 1].Length == 0) replacement.RemoveAt(replacement.Count - 1);
            var body = string.Join(lineBreak, replacement);

            var first = startLine - 1;
            var end = endLine;
            var last = totalLines - 1;
            if (end < totalLines)
                return new LineEdit(first, 0, end, 0, replacement.Count > 0 ? body + lineBreak : "");
            if (first == 0)
                return new LineEdit(0, 0, last, lengthOfLine(last), body);
            return new LineEdit(first - 1, lengthOfLine(first - 1), last, lengthOfLine(last), replacement.Count > 0 ? lineBreak + body : "");
        }
    }
}
