using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// The minimal Markdown subset chat answers use (fenced code, inline code, bold, italic, links, headings, nested
/// lists, pipe tables). Produces a block model; raw HTML stays literal text.
/// </summary>
internal static class Markdown
{
    private static readonly Regex Fence = new Regex(@"^\s*```\s*([\w+#.-]*)\s*$", RegexOptions.Compiled);
    private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new Regex(@"^(\s*)([-*+]|[0-9]{1,9}[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$", RegexOptions.Compiled);
    private static readonly Regex CodeSpan = new Regex(@"(`[^`\n]+`)", RegexOptions.Compiled);

    private static readonly Regex Span = new Regex(
        @"\[(?<lt>[^\[\]\n]+)\]\((?<lu>[^)\s]+)\)" +
        @"|\*\*(?<b>[^*\n]+)\*\*" +
        @"|(?<![*\w])\*(?<i>[^*\s](?:[^*\n]*[^*\s])?)\*(?![*\w])" +
        @"|(?<!\w)_(?<u>[^_\s](?:[^_\n]*[^_\s])?)_(?!\w)",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a Markdown-formatted string into a read-only list of block-level elements, such as paragraphs, lists, and code blocks.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A collection of iread only list items.</returns>
    public static IReadOnlyList<MdBlock> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new List<MdBlock>();
        var prose = new List<string>();
        var list = new List<string>();
        List<string>? code = null;
        var lang = "";

        void FlushProse()
        {
            if (prose.Count > 0)
            {
                output.Add(new MdParagraph(prose.Select(ParseInline).ToList()));
            }

            prose.Clear();
        }

        void FlushList()
        {
            if (list.Count > 0)
            {
                output.AddRange(ParseList(list));
            }

            list.Clear();
        }

        void FlushCode()
        {
            output.Add(new MdCodeBlock(lang.Length > 0 ? lang : null, string.Join("\n", code!)));
            code = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var fence = Fence.Match(line);
            if (code is not null)
            {
                if (fence.Success && fence.Groups[1].Value.Length == 0)
                {
                    FlushCode();
                }
                else
                {
                    code.Add(line);
                }
            }
            else if (fence.Success)
            {
                FlushProse();
                FlushList();
                code = new List<string>();
                lang = fence.Groups[1].Value;
            }
            else if (line.Trim().Length == 0)
            {
                FlushProse();
                var next = lines.Skip(index + 1).FirstOrDefault(candidate => candidate.Trim().Length > 0);
                if (next is null || !(ListItem.IsMatch(next) || StartsWithWhitespace(next)))
                {
                    FlushList();
                }
            }
            else if (ListItem.IsMatch(line) || (list.Count > 0 && StartsWithWhitespace(line)))
            {
                FlushProse();
                list.Add(line);
            }
            else if (line.Trim().StartsWith("|", StringComparison.Ordinal) && index + 1 < lines.Length && lines[index + 1].Contains("|") && TableSeparator.IsMatch(lines[index + 1]))
            {
                FlushProse();
                FlushList();
                var rows = new List<string>();
                var next = index + 2;
                while (next < lines.Length && lines[next].Trim().StartsWith("|", StringComparison.Ordinal))
                {
                    rows.Add(lines[next++]);
                }

                output.Add(ParseTable(line, lines[index + 1], rows));
                index = next - 1;
            }
            else if (Heading.Match(line) is Match heading && heading.Success)
            {
                FlushProse();
                FlushList();
                output.Add(new MdHeading(Math.Min(6, heading.Groups[1].Value.Length + 2), ParseInline(heading.Groups[2].Value)));
            }
            else
            {
                FlushList();
                prose.Add(line);
            }
        }
        if (code is not null)
        {
            FlushCode();
        }

        FlushProse();
        FlushList();

        return output;
    }

    /// <summary>
    /// Determines whether the specified string starts with a whitespace character.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    private static bool StartsWithWhitespace(string line) => line.Length > 0 && char.IsWhiteSpace(line[0]);

    /// <summary>Nested lists from list item lines; indented non-item lines continue the previous item.</summary>
    private static IEnumerable<MdList> ParseList(List<string> lines)
    {
        var roots = new List<MdList>();
        var stack = new List<(int Indent, MdList List)>();
        MdListItem? current = null;
        foreach (var line in lines)
        {
            var item = ListItem.Match(line);
            if (!item.Success)
            {
                if (current is not null)
                {
                    current.Raw += " " + line.Trim();
                }

                continue;
            }
            var indent = item.Groups[1].Value.Length;
            var marker = item.Groups[2].Value;
            var ordered = char.IsDigit(marker[0]);
            while (stack.Count > 0 && indent < stack[stack.Count - 1].Indent)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var top = stack.Count > 0 ? stack[stack.Count - 1] : ((int Indent, MdList List)?)null;
            if (!(top.HasValue && indent == top.Value.Indent && top.Value.List.Ordered == ordered))
            {
                if (top.HasValue && indent == top.Value.Indent)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                var start = ordered ? int.Parse(marker.Substring(0, marker.Length - 1), CultureInfo.InvariantCulture) : 1;
                var created = new MdList(ordered, start);
                if (stack.Count > 0)
                {
                    var parentItems = stack[stack.Count - 1].List.Items;
                    parentItems[parentItems.Count - 1].Children.Add(created);
                }
                else
                {
                    roots.Add(created);
                }
                stack.Add((indent, created));
            }
            current = new MdListItem { Raw = item.Groups[3].Value };
            stack[stack.Count - 1].List.Items.Add(current);
        }
        foreach (var root in roots)
        {
            ParseItems(root);
        }

        return roots;
    }

    /// <summary>
    /// Recursively parses the inline content of each item within the specified markdown list and its nested children.
    /// </summary>
    /// <param name="list">The list.</param>
    private static void ParseItems(MdList list)
    {
        foreach (var item in list.Items)
        {
            item.Inlines = ParseInline(item.Raw);
            foreach (var child in item.Children)
            {
                ParseItems(child);
            }
        }
    }

    /// <summary>
    /// Parses a Markdown-style table row by removing surrounding pipe characters and splitting the line into a list of trimmed cell values.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>A collection of iread only list items.</returns>
    private static IReadOnlyList<string> TableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("|", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1);
        }

        if (trimmed.EndsWith("|", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return trimmed.Split('|').Select(cell => cell.Trim()).ToList();
    }

    /// <summary>
    /// Parses a Markdown table by processing the header, alignment separator, and row data into a structured MdTable object.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <param name="separator">The separator.</param>
    /// <param name="rows">The collection of rows.</param>
    /// <returns>The md table result.</returns>
    private static MdTable ParseTable(string header, string separator, List<string> rows)
    {
        var aligns = TableCells(separator).Select(cell =>
            cell.StartsWith(":", StringComparison.Ordinal) && cell.EndsWith(":", StringComparison.Ordinal) ? MdAlign.Center
            : cell.EndsWith(":", StringComparison.Ordinal) ? MdAlign.Right : MdAlign.Left).ToList();
        IReadOnlyList<IReadOnlyList<MdInline>> Cells(string row) => TableCells(row).Select(ParseInline).ToList();

        return new MdTable(Cells(header), aligns, rows.Select(Cells).ToList());
    }

    /// <summary>Stands in for the n-th code span while links, bold and italic are found, so nothing inside code is interpreted.</summary>
    private static readonly Regex CodeMark = new Regex("\uE000(\\d+)\uE001", RegexOptions.Compiled);

    /// <summary>
    /// Inline spans: <c>`code`</c> (nothing inside it is interpreted), links, bold and italic; bold and italic may hold code.
    /// </summary>
    public static IReadOnlyList<MdInline> ParseInline(string text)
    {
        var codes = new List<string>();
        var masked = CodeSpan.Replace(text, span =>
        {
            codes.Add(span.Value.Substring(1, span.Value.Length - 2));

            return "\uE000" + (codes.Count - 1).ToString(CultureInfo.InvariantCulture) + "\uE001";
        });
        var result = new List<MdInline>();
        var position = 0;
        foreach (Match match in Span.Matches(masked))
        {
            if (match.Index > position)
            {
                result.AddRange(Unmask(masked.Substring(position, match.Index - position), codes));
            }

            if (match.Groups["lt"].Success)
            {
                result.Add(new MdInline(MdInlineKind.Link, Plain(match.Groups["lt"].Value, codes), Plain(match.Groups["lu"].Value, codes)));
            }
            else if (match.Groups["b"].Success)
            {
                result.Add(Emphasis(MdInlineKind.Bold, match.Groups["b"].Value, codes));
            }
            else if (match.Groups["i"].Success)
            {
                result.Add(Emphasis(MdInlineKind.Italic, match.Groups["i"].Value, codes));
            }
            else
            {
                result.Add(Emphasis(MdInlineKind.Italic, match.Groups["u"].Value, codes));
            }

            position = match.Index + match.Length;
        }
        if (position < masked.Length)
        {
            result.AddRange(Unmask(masked.Substring(position), codes));
        }

        return result;
    }

    /// <summary>
    /// Creates an emphasis inline element by unmasking the provided text and conditionally nesting child elements if code segments are detected.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="masked">The masked.</param>
    /// <param name="codes">The collection of codes.</param>
    /// <returns>The md inline result.</returns>
    private static MdInline Emphasis(MdInlineKind kind, string masked, List<string> codes)
    {
        var children = Unmask(masked, codes).ToList();

        return children.Any(child => child.Kind == MdInlineKind.Code)
            ? new MdInline(kind, Plain(masked, codes), children: children)
            : new MdInline(kind, Plain(masked, codes));
    }

    /// <summary>Text and code spans of <paramref name="masked"/>, with each code mark restored.</summary>
    private static IEnumerable<MdInline> Unmask(string masked, List<string> codes)
    {
        var position = 0;
        foreach (Match mark in CodeMark.Matches(masked))
        {
            if (!int.TryParse(mark.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= codes.Count)
            {
                continue;
            }

            if (mark.Index > position)
            {
                yield return new MdInline(MdInlineKind.Text, masked.Substring(position, mark.Index - position));
            }

            yield return new MdInline(MdInlineKind.Code, codes[index]);
            position = mark.Index + mark.Length;
        }
        if (position < masked.Length)
        {
            yield return new MdInline(MdInlineKind.Text, masked.Substring(position));
        }
    }

    /// <summary>
    /// Replaces masked placeholders within a string with their corresponding values from a provided list of codes based on the index specified in each placeholder.
    /// </summary>
    /// <param name="masked">The masked.</param>
    /// <param name="codes">The collection of codes.</param>
    /// <returns>The string result.</returns>
    private static string Plain(string masked, List<string> codes) =>
        CodeMark.Replace(masked, mark => int.TryParse(mark.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < codes.Count ? codes[index] : mark.Value);

    /// <summary>A value equal for two blocks exactly when they render the same; lets a re-render keep unchanged blocks.</summary>
    public static string Key(MdBlock block)
    {
        var key = new System.Text.StringBuilder();
        void Inlines(IReadOnlyList<MdInline> inlines)
        {
            foreach (var inline in inlines)
            {
                key.Append((int)inline.Kind).Append('\u0001').Append(inline.Text).Append('\u0001').Append(inline.Url).Append('\u0002');
                if (inline.Children.Count > 0)
                {
                    Inlines(inline.Children);
                }
            }
            key.Append('\u0003');
        }
        void List(MdList list)
        {
            key.Append(list.Ordered ? 'o' : 'u').Append(list.Start).Append('\u0004');
            foreach (var item in list.Items)
            {
                Inlines(item.Inlines);
                foreach (var child in item.Children)
                {
                    List(child);
                }

                key.Append('\u0005');
            }
            key.Append('\u0006');
        }
        switch (block)
        {
            case MdParagraph paragraph:
                key.Append('p');
                foreach (var line in paragraph.Lines)
                {
                    Inlines(line);
                }

                break;

            case MdHeading heading:
                key.Append('h').Append(heading.Level);
                Inlines(heading.Inlines);
                break;

            case MdCodeBlock code:
                key.Append('c').Append(code.Language).Append('\u0001').Append(code.Code);
                break;

            case MdList list:
                key.Append('l');
                List(list);
                break;

            case MdTable table:
                key.Append('t').Append(string.Join(",", table.Aligns));
                foreach (var cell in table.Header)
                {
                    Inlines(cell);
                }

                foreach (var row in table.Rows)
                {
                    key.Append('\u0004');
                    foreach (var cell in row)
                    {
                        Inlines(cell);
                    }
                }
                break;
        }

        return key.ToString();
    }
}
