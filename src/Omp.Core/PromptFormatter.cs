using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Omp.Core;

/// <summary>
/// Wire format of a user prompt: <see cref="Format"/> writes it before sending to OMP and
/// <see cref="SplitUserMessage"/> reads it back to render the user's message.
/// Layout, parts separated by a blank line: optional <c>&lt;editor-context&gt;…&lt;/editor-context&gt;</c> block,
/// the typed text, one <c>&lt;pasted-text name="…"&gt;</c> block per pasted text, then <c>Attached: @a @"b c"</c>.
/// </summary>
public static class PromptFormatter
{
    private const string EditorContextStart = "<editor-context>";
    private const string EditorContextEnd = "</editor-context>";
    private const string PastedEnd = "</pasted-text>";
    private const string PastedEndEscaped = "<\\/pasted-text>";
    private static readonly Regex PastedBlock = new Regex("\n*<pasted-text name=\"([^\"]*)\">\n([\\s\\S]*?)\n</pasted-text>", RegexOptions.CultureInvariant);
    private static readonly Regex AttachedLine = new Regex("\n*Attached: ((?:@(?:\"(?:\\\\\"|[^\"])+\"|\\S+) ?)+)\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Mention = new Regex("@(?:\"((?:\\\\\"|[^\"])+)\"|(\\S+))", RegexOptions.CultureInvariant);
    private static readonly Regex NeedsQuotes = new Regex("[\\s\"@]", RegexOptions.CultureInvariant);

    public static string Format(PromptParts parts)
    {
        var blocks = new List<string>();
        if (!string.IsNullOrEmpty(parts.EditorContext))
        {
            blocks.Add(parts.EditorContext!);
        }

        if (!string.IsNullOrEmpty(parts.Text))
        {
            blocks.Add(parts.Text);
        }

        foreach (var block in parts.Pasted)
        {
            blocks.Add($"<pasted-text name=\"{block.Name.Replace('"', '\'')}\">\n{block.Text.Replace(PastedEnd, PastedEndEscaped)}\n{PastedEnd}");
        }
        if (parts.Files.Count > 0)
        {
            blocks.Add("Attached: " + string.Join(" ", parts.Files.Select(FileMention)));
        }

        return string.Join("\n\n", blocks);
    }

    public static PromptParts SplitUserMessage(string message)
    {
        var rest = message;
        string? editorContext = null;
        var end = rest.IndexOf(EditorContextEnd, StringComparison.Ordinal);
        if (rest.StartsWith(EditorContextStart, StringComparison.Ordinal) && end >= 0)
        {
            editorContext = rest.Substring(0, end + EditorContextEnd.Length);
            rest = rest.Substring(end + EditorContextEnd.Length).TrimStart('\n');
        }
        var files = new List<string>();
        var attached = AttachedLine.Match(rest);
        if (attached.Success && (attached.Index == 0 || rest[attached.Index] == '\n'))
        {
            foreach (Match mention in Mention.Matches(attached.Groups[1].Value))
            {
                files.Add(mention.Groups[1].Success ? mention.Groups[1].Value.Replace("\\\"", "\"") : mention.Groups[2].Value);
            }
            rest = rest.Substring(0, attached.Index);
        }
        var pasted = new List<PastedText>();
        rest = PastedBlock.Replace(rest, block =>
        {
            pasted.Add(new PastedText { Name = block.Groups[1].Value, Text = block.Groups[2].Value.Replace(PastedEndEscaped, PastedEnd) });

            return "";
        });

        return new PromptParts { Text = rest.Trim(), EditorContext = editorContext, Pasted = pasted, Files = files };
    }

    /// <summary><c>@path</c>, quoted (with <c>"</c> escaped) when OMP's mention syntax requires it.</summary>
    private static string FileMention(string path) =>
        NeedsQuotes.IsMatch(path) ? "@\"" + path.Replace("\"", "\\\"") + "\"" : "@" + path;
}
