using Newtonsoft.Json.Linq;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Pure tool-call summaries for the collapsed tool rows; never throws.</summary>
internal static class ToolFormat
{
    /// <summary>Longest one-line summary shown in a collapsed row.</summary>
    public const int MaxSummary = 120;
    private const int MaxErrorLine = 160;
    private static readonly string[] SummaryKeys = ["action", "command", "pattern", "query", "url", "path", "file", "agent"];
    private static readonly string[] ListKeys = ["paths", "ids"];
    private static readonly string[] HitCountKeys = ["matchCount", "totalMatches", "count", "fileCount"];
    private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SelectorSuffix = new Regex(@":(raw|[0-9,+\-]+)$", RegexOptions.Compiled);
    private static readonly Regex LeadingNumber = new Regex(@"^\d+", RegexOptions.Compiled);

    private static readonly Dictionary<string, RendererKind> RendererByName = new Dictionary<string, RendererKind>(StringComparer.Ordinal)
    {
        ["read"] = RendererKind.File,
        ["write"] = RendererKind.File,
        ["edit"] = RendererKind.File,
        ["ast_edit"] = RendererKind.File,
        ["grep"] = RendererKind.Search,
        ["glob"] = RendererKind.Search,
        ["ast_grep"] = RendererKind.Search,
        ["bash"] = RendererKind.Shell,
        ["eval"] = RendererKind.Shell,
        ["task"] = RendererKind.Task,
        ["lsp"] = RendererKind.Lsp,
        ["debug"] = RendererKind.Lsp,
    };

    public static RendererKind PickRenderer(string name)
    {
        if (name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return RendererKind.Mcp;
        }

        return RendererByName.TryGetValue(name, out var kind) ? kind : RendererKind.Generic;
    }

    private static string OneLine(string text)
    {
        var flat = Whitespace.Replace(text, " ").Trim();

        return flat.Length > MaxSummary ? flat.Substring(0, MaxSummary - 1) + "…" : flat;
    }

    /// <summary>Non-empty string value, else null.</summary>
    public static string? Str(JToken? token) =>
        token is not null && token.Type == JTokenType.String && token.Value<string>() is string s && s.Length > 0 ? s : null;

    /// <summary>Whole-number value of a JSON number (fractions truncate); null for non-numbers and values outside <see cref="long"/>.</summary>
    private static long? Num(JToken? token)
    {
        if (token is null)
        {
            return null;
        }

        if (token.Type == JTokenType.Integer)
        {
            return ((JValue)token).Value is System.Numerics.BigInteger ? (long?)null : token.Value<long>();
        }

        if (token.Type != JTokenType.Float)
        {
            return null;
        }

        var value = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);

        return value >= long.MinValue && value < long.MaxValue ? (long?)(long)value : null;
    }

    public static IReadOnlyList<string> TaskNames(JToken? tasks)
    {
        if (!(tasks is JArray array))
        {
            return Array.Empty<string>();
        }

        return array.Select((task, index) =>
        {
            if (task is JObject record)
            {
                var label = new[] { record["name"], record["agent"], record["id"] }.Select(Str).FirstOrDefault(v => v != null);
                if (label is not null)
                {
                    return label;
                }
            }

            return $"#{index + 1}";
        }).ToList();
    }

    /// <summary>One-line description of a tool call from its arguments.</summary>
    public static string SummarizeTool(string name, JToken? args)
    {
        if (args is null)
        {
            return "";
        }

        switch (args.Type)
        {
            case JTokenType.String:
                return OneLine(args.Value<string>() ?? "");

            case JTokenType.Integer:
            case JTokenType.Float:
                return Convert.ToString(((JValue)args).Value, CultureInfo.InvariantCulture) ?? "";

            case JTokenType.Boolean:
                return args.Value<bool>() ? "true" : "false";
        }
        if (!(args is JObject record))
        {
            return "";
        }

        if (name == "task" && record["tasks"] is JArray tasks)
        {
            var count = tasks.Count;

            return OneLine($"{count} task{(count == 1 ? "" : "s")}: {string.Join(", ", TaskNames(tasks))}");
        }
        var parts = SummaryKeys.Select(key => record[key]).Where(v => v != null && v.Type == JTokenType.String && v.Value<string>()!.Trim().Length > 0).Select(v => v!.Value<string>()!).ToList();
        if (parts.Count > 0)
        {
            return OneLine(string.Join(" · ", parts.Take(2)));
        }

        foreach (var key in ListKeys)
        {
            if (record[key] is JArray list)
            {
                return OneLine(string.Join(", ", list.Where(v => v.Type == JTokenType.String).Select(v => v.Value<string>())));
            }
        }
        var firstString = record.Properties().Select(p => p.Value).FirstOrDefault(v => v.Type == JTokenType.String);

        return firstString is not null ? OneLine(firstString.Value<string>() ?? "") : "";
    }

    public static (string Server, string Tool) SplitMcpName(string name)
    {
        var rest = name.StartsWith("mcp__", StringComparison.Ordinal) ? name.Substring(5) : name;
        var separator = rest.Contains("__") ? "__" : "_";
        var index = rest.IndexOf(separator, StringComparison.Ordinal);

        return index < 0 ? (rest, "") : (rest.Substring(0, index), rest.Substring(index + separator.Length));
    }

    /// <summary>
    /// For each diff line, the part of its text (after the +/- sign) that differs from the line it replaces, or null.
    /// A run of removed lines followed by a run of added lines is paired line by line; within a pair the common
    /// start and end are left out, widened to whole words, so a change of one or two words marks just those words.
    /// A pair with nothing in common is left unmarked: the whole line is the change.
    /// </summary>
    public static (int Start, int Length)?[] ChangedSpans(IReadOnlyList<string> lines)
    {
        var spans = new (int Start, int Length)?[lines.Count];
        var i = 0;
        while (i < lines.Count)
        {
            if (DiffLineClass(lines[i]) != DiffLineKind.Delete)
            {
                i++;
                continue;
            }
            var removedStart = i;
            while (i < lines.Count && DiffLineClass(lines[i]) == DiffLineKind.Delete)
            {
                i++;
            }

            var addedStart = i;
            while (i < lines.Count && DiffLineClass(lines[i]) == DiffLineKind.Add)
            {
                i++;
            }

            var pairs = Math.Min(addedStart - removedStart, i - addedStart);
            for (var p = 0; p < pairs; p++)
            {
                var removed = lines[removedStart + p].Substring(1);
                var added = lines[addedStart + p].Substring(1);
                var (removedSpan, addedSpan) = Differing(removed, added);
                spans[removedStart + p] = removedSpan;
                spans[addedStart + p] = addedSpan;
            }
        }

        return spans;
    }

    private static ((int, int)?, (int, int)?) Differing(string removed, string added)
    {
        var prefix = 0;
        while (prefix < removed.Length && prefix < added.Length && removed[prefix] == added[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < removed.Length - prefix && suffix < added.Length - prefix
            && removed[removed.Length - 1 - suffix] == added[added.Length - 1 - suffix])
        {
            suffix++;
        }

        while (prefix > 0 && IsWordChar(removed[prefix - 1]) && (InWord(removed, prefix) || InWord(added, prefix)))
        {
            prefix--;
        }

        while (suffix > 0 && IsWordChar(removed[removed.Length - suffix])
            && (InWord(removed, removed.Length - suffix - 1) || InWord(added, added.Length - suffix - 1)))
        {
            suffix--;
        }

        if (prefix + suffix == 0)
        {
            return (null, null);
        }

        return (Span(removed.Length, prefix, suffix), Span(added.Length, prefix, suffix));
    }

    private static (int, int)? Span(int length, int prefix, int suffix) =>
        length - prefix - suffix > 0 ? (prefix, length - prefix - suffix) : ((int, int)?)null;

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool InWord(string text, int index) => index >= 0 && index < text.Length && IsWordChar(text[index]);

    public static DiffLineKind DiffLineClass(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
        {
            return DiffLineKind.Meta;
        }

        if (line.StartsWith("+", StringComparison.Ordinal))
        {
            return DiffLineKind.Add;
        }

        if (line.StartsWith("-", StringComparison.Ordinal))
        {
            return DiffLineKind.Delete;
        }

        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            return DiffLineKind.Hunk;
        }

        return DiffLineKind.Context;
    }

    /// <summary>Whether tool output is a unified diff: it has a <c>diff --git</c> header or an <c>@@</c> hunk line.</summary>
    public static bool LooksLikeDiff(string text) =>
        text.Split('\n').Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("@@ ", StringComparison.Ordinal));

    public static long? HitCount(ToolResultView? result)
    {
        if (result is null)
        {
            return null;
        }

        if (result.Details is JObject details)
        {
            foreach (var key in HitCountKeys)
            {
                var value = Num(details[key]);
                if (value.HasValue)
                {
                    return value;
                }
            }
        }

        return result.Text.Split('\n').Count(line => line.Trim().Length > 0);
    }

    public static long? ExitCodeOf(ToolResultView? result) => result?.Details is JObject details ? Num(details["exitCode"]) : null;

    public static string ShellCommand(ToolItem item)
    {
        var args = item.Args as JObject;

        return Str(args?["command"]) ?? Str(args?["code"]) ?? Str(args?["input"]) ?? SummarizeTool(item.Name, item.Args);
    }

    private static readonly string[] DescriptionKeys = ["i", "title"];

    /// <summary>What the agent said a call is for (OMP's <c>i</c> intent or an <c>eval</c> title); null when it gave none.</summary>
    public static string? Description(ToolItem item)
    {
        var args = item.Args as JObject;

        return DescriptionKeys.Select(key => Str(args?[key])).FirstOrDefault(value => value != null);
    }

    private static readonly Regex TrailerLine = new Regex(@"^(Wall time: .*|Command exited with code \d+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Shell output without the timing and exit-code lines OMP appends to it and the blank lines around them: the
    /// header already carries the exit code. Lines of that shape inside the output stay.
    /// </summary>
    public static string StripShellTrailer(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && (lines[lines.Count - 1].Trim().Length == 0 || TrailerLine.IsMatch(lines[lines.Count - 1])))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines);
    }

    private static readonly Regex FenceLine = new Regex(@"^\s*```([\w#+.-]*)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Tool output without the Markdown code fences around its code, and the language the first labeled fence names
    /// (null when none does): the output is shown as code anyway, so the fences only add noise.
    /// </summary>
    public static (string Text, string? Language) StripFences(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        string? language = null;
        foreach (var line in lines)
        {
            var fence = FenceLine.Match(line);
            if (!fence.Success)
            {
                kept.Add(line);
                continue;
            }

            if (language is null && fence.Groups[1].Value.Length > 0)
            {
                language = fence.Groups[1].Value;
            }
        }

        return (string.Join("\n", kept), language);
    }

    private static readonly Regex MarkdownHeading = new Regex(@"^#{1,6} \S", RegexOptions.Compiled);

    /// <summary>
    /// What a search tool looked for, to mark in its result: its pattern as a regex, or as literal text when it is
    /// not a valid one, ignoring case when the call sets <c>case</c> to false; null for other tools or a call without a pattern.
    /// </summary>
    public static Regex? SearchPattern(ToolItem item)
    {
        if (PickRenderer(item.Name) != RendererKind.Search)
        {
            return null;
        }

        var args = item.Args as JObject;
        var pattern = Str(args?["pattern"]);
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        var caseToken = args!["case"];
        var ignoreCase = caseToken is not null && caseToken.Type == JTokenType.Boolean && !caseToken.Value<bool>() ? RegexOptions.IgnoreCase : RegexOptions.None;
        try
        {
            return new Regex(pattern!, RegexOptions.Multiline | ignoreCase, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return new Regex(Regex.Escape(pattern!), ignoreCase, TimeSpan.FromMilliseconds(200));
        }
    }

    /// <summary>The extension of the file a file tool names, without its read selector (lower case, without the dot), which is the language of its result; null for other tools or a file without one.</summary>
    public static string? FileLanguage(ToolItem item)
    {
        if (PickRenderer(item.Name) != RendererKind.File || !(item.Args is JObject args))
        {
            return null;
        }

        var raw = Str(args["path"]) ?? Str(args["file"]) ?? Str(args["file_path"]);
        if (raw is null)
        {
            return null;
        }

        var path = SplitSelector(raw).Path;
        var name = path.Substring(path.LastIndexOfAny(['/', '\\']) + 1);
        var dot = name.LastIndexOf('.');

        return dot > 0 && dot < name.Length - 1 ? name.Substring(dot + 1).ToLowerInvariant() : null;
    }

    /// <summary>The text a result renders as prose: a numbered file listing loses its header and line-number prefixes first.</summary>
    public static string ProseText(string text) => SplitLineNumbers(text)?.Code ?? text;

    /// <summary>
    /// Whether a call's result should render as Markdown prose rather than code: a read of a Markdown file, or a
    /// text tool whose result looks like Markdown; never a failed call.
    /// </summary>
    public static bool RendersMarkdown(ToolItem item)
    {
        var text = item.Result?.Text;
        if (text is null || item.Result?.IsError == true)
        {
            return false;
        }

        if (item.Name == "read")
        {
            var language = FileLanguage(item);
            var path = Str((item.Args as JObject)?["path"]) ?? "";

            return language == "md" || language == "markdown" || (path.Contains("://") && LooksLikeMarkdown(text));
        }

        var kind = PickRenderer(item.Name);

        return (kind == RendererKind.Mcp || kind == RendererKind.Generic) && LooksLikeMarkdown(text);
    }

    /// <summary>A file listing OMP numbered: the optional <c>[path#id]</c> header, one number (or range) per line, and the code without them.</summary>
    public sealed class Listing
    {
        public Listing(string? header, IReadOnlyList<string> numbers, string code)
        {
            Header = header;
            Numbers = numbers;
            Code = code;
        }

        public string? Header { get; }

        public IReadOnlyList<string> Numbers { get; }

        public string Code { get; }
    }

    /// <summary>
    /// Splits a read result into its line-number gutter and clean code. Lines carry an <c>N:</c>, <c>*N:</c> or
    /// <c>N-M:</c> prefix; null unless at least one does, each numbered line directly below another starts after
    /// the line or range above it ends (gaps allowed), and, without a <c>[path#id]</c> header, numbered lines make
    /// up at least 60% of the non-blank lines that are not OMP's <c>…</c> elisions or <c>[…]</c> notices.
    /// </summary>
    public static Listing? SplitLineNumbers(string text)
    {
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var start = 0;
        string? header = null;
        if (lines.Length > 0 && IsBracketed(lines[0]) && lines[0].IndexOf('#') > 1)
        {
            header = lines[0];
            start = 1;
        }

        if (start >= lines.Length)
        {
            return null;
        }

        var numbers = new List<string>(lines.Length - start);
        var code = new string[lines.Length - start];
        var numbered = 0;
        var filled = 0;
        long? previousEnd = null;
        for (var i = start; i < lines.Length; i++)
        {
            var match = NumberedLine.Match(lines[i]);
            var trimmed = lines[i].Trim();
            filled += match.Success || (trimmed.Length > 0 && trimmed != "…" && !IsBracketed(trimmed)) ? 1 : 0;
            if (match.Success)
            {
                if (!long.TryParse(match.Groups["from"].Value, out var from)
                    || !long.TryParse(match.Groups["to"].Success ? match.Groups["to"].Value : match.Groups["from"].Value, out var to)
                    || from <= previousEnd)
                {
                    return null;
                }

                numbered++;
                previousEnd = to;
            }
            else
            {
                previousEnd = null;
            }

            numbers.Add(match.Success ? match.Groups["n"].Value : "");
            code[i - start] = match.Success ? match.Groups["rest"].Value : lines[i];
        }

        if (numbered == 0 || (header is null && numbered * 5 < filled * 3))
        {
            return null;
        }

        return new Listing(header, numbers, string.Join("\n", code));
    }

    private static bool IsBracketed(string line) => line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal);

    private static readonly Regex NumberedLine = new Regex(@"^(?<n>\*?(?<from>\d+)(?:-(?<to>\d+))?):(?<rest>.*)$", RegexOptions.Compiled);

    /// <summary>Whether tool output is Markdown prose: an ATX heading line and at least one other non-blank line, which code and plain output lack.</summary>
    public static bool LooksLikeMarkdown(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var headings = 0;
        var others = 0;
        foreach (var line in lines)
        {
            if (MarkdownHeading.IsMatch(line))
            {
                headings++;
            }
            else if (line.Trim().Length > 0)
            {
                others++;
            }
        }

        return headings > 0 && others > 0;
    }

    /// <summary>The language of a shell call's command: the language an <c>eval</c> names, <c>bash</c> for the shell; null for other tools.</summary>
    public static string? ShellLanguage(ToolItem item)
    {
        if (item.Name == "eval")
        {
            return Str((item.Args as JObject)?["language"]);
        }

        return item.Name == "bash" ? "bash" : null;
    }

    /// <summary>
    /// What a tool call really is: running, failed, a job it only started in the background (its result
    /// acknowledges the start; the work is still going), or done.
    /// </summary>
    public static ToolState StateOf(ToolItem item)
    {
        if (item.Status == ToolStatus.Running)
        {
            return ToolState.Running;
        }

        if (item.Status == ToolStatus.Error || item.Result?.IsError == true)
        {
            return ToolState.Failed;
        }

        var job = (item.Result?.Details as JObject)?["async"] as JObject;

        return Str(job?["state"]) == "running" ? ToolState.Background : ToolState.Done;
    }

    /// <summary>Whether the header's one-line summary shows <paramref name="command"/> whole: a single line without extra whitespace that is not cut.</summary>
    public static bool FitsHeader(string command) => command.Length <= MaxSummary && Whitespace.Replace(command, " ").Trim() == command;

    /// <summary>The arguments of a shell tool call other than its command or code and the intent the header shows, one <c>name: value</c> per line.</summary>
    public static string ShellParameters(ToolItem item)
    {
        if (!(item.Args is JObject args))
        {
            return "";
        }

        var commandKey = new[] { "command", "code", "input" }.FirstOrDefault(key => Str(args[key]) != null);
        var descriptionKey = DescriptionKeys.FirstOrDefault(key => Str(args[key]) != null);

        return FlatLines(args.Properties().Where(p => p.Name != commandKey && p.Name != descriptionKey));
    }

    /// <summary>
    /// A call's arguments for reading: an object as one <c>name: value</c> line per property (strings bare, their
    /// further lines indented; everything else as JSON, nested values on one line); anything else as indented JSON.
    /// </summary>
    public static string FlatArgs(JToken args) =>
        args is JObject record ? FlatLines(record.Properties()) : IndentedJson(args);

    /// <summary>Indented JSON with <c>\n</c> line ends, as copying a tool's arguments yields them.</summary>
    public static string IndentedJson(JToken token) => token.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n");

    private static string FlatLines(IEnumerable<JProperty> properties) =>
        string.Join("\n", properties.Select(p => $"{p.Name}: {FlatValue(p.Value)}"));

    private static string FlatValue(JToken value) =>
        value.Type == JTokenType.String
            ? (value.Value<string>() ?? "").Replace("\r\n", "\n").Replace("\n", "\n  ")
            : value.ToString(Newtonsoft.Json.Formatting.None);

    private static string? SearchScope(JObject args)
    {
        var path = Str(args["path"]);
        if (path is not null)
        {
            return path;
        }

        if (!(args["paths"] is JArray paths))
        {
            return null;
        }

        var joined = string.Join(", ", paths.Where(p => p.Type == JTokenType.String).Select(p => p.Value<string>()));

        return joined.Length > 0 ? joined : null;
    }

    private static string PrimaryArg(ToolItem item, RendererKind kind)
    {
        var args = item.Args as JObject ?? new JObject();
        switch (kind)
        {
            case RendererKind.File:
                {
                    var details = item.Result?.Details as JObject ?? new JObject();

                    return OneLine(Str(args["path"]) ?? Str(args["file"]) ?? Str(details["path"]) ?? Str(details["resolvedPath"]) ?? SummarizeTool(item.Name, item.Args));
                }
            case RendererKind.Search:
                {
                    var pattern = Str(args["pattern"]) ?? Str(args["query"]) ?? SummarizeTool(item.Name, item.Args);
                    var where = SearchScope(args);

                    return pattern.Length > 0 ? OneLine($"\"{pattern}\"{(where is not null ? $" in {where}" : "")}") : "";
                }
            case RendererKind.Shell:
                return OneLine(Description(item) ?? ShellCommand(item));

            case RendererKind.Lsp:
                {
                    var text = OneLine(string.Join(" ", new[] { Str(args["action"]) ?? Str(args["op"]), Str(args["file"]) ?? Str(args["path"]) }.Where(p => p != null)));

                    return text.Length > 0 ? text : SummarizeTool(item.Name, item.Args);
                }
            default:
                return SummarizeTool(item.Name, item.Args);
        }
    }

    private static string? ErrorLine(ToolResultView? result)
    {
        if (result is null || !result.IsError)
        {
            return null;
        }

        var line = result.Text.Split('\n').Select(candidate => candidate.Trim()).FirstOrDefault(candidate => candidate.Length > 0);
        if (line is null)
        {
            return null;
        }

        return line.Length > MaxErrorLine ? line.Substring(0, MaxErrorLine - 1) + "…" : line;
    }

    /// <summary>Rewrites paths under <paramref name="cwd"/> as relative paths, matching either separator and any letter case.</summary>
    private static string RelativeTo(string text, string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
        {
            return text;
        }

        var root = cwd!.TrimEnd('\\', '/');
        if (root.Length == 0)
        {
            return text;
        }

        var pattern = string.Join(@"[\\/]", root.Split('\\', '/').Select(Regex.Escape)) + @"[\\/]";

        return Regex.Replace(text, "(^|[\\s\"'(])" + pattern, "$1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Label of a file link: the path relative to <paramref name="cwd"/> when inside it, plus <c>:line</c>.</summary>
    public static string FileLinkLabel(string path, int? line, string? cwd)
    {
        var shown = RelativeTo(path, cwd);

        return line.HasValue && line.Value != 0 ? $"{shown}:{line}" : shown;
    }

    /// <summary><c>1 line</c> / <c>3 lines</c>.</summary>
    public static string Plural(long count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    public static ToolHeadline Headline(ToolItem item, string? cwd)
    {
        var state = StateOf(item);
        var glyph = state == ToolState.Running || state == ToolState.Background ? "…" : state == ToolState.Failed ? "✗" : "✓";
        var kind = PickRenderer(item.Name);
        try
        {
            var (server, tool) = SplitMcpName(item.Name);
            var name = kind == RendererKind.Mcp ? (tool.Length > 0 ? $"{server} › {tool}" : server) : item.Name;
            var parts = new List<string>();
            if (kind == RendererKind.Search && item.Status != ToolStatus.Running && item.Result?.IsError != true)
            {
                var hits = HitCount(item.Result);
                if (hits.HasValue)
                {
                    parts.Add(Plural(hits.Value, "hit"));
                }
            }
            var exit = kind == RendererKind.Shell ? ExitCodeOf(item.Result) : null;
            if (exit.HasValue)
            {
                parts.Add($"exit {exit}");
            }

            if (item.Name == "write" && item.Args is JObject args && args["content"]?.Type == JTokenType.String)
            {
                parts.Add(Plural(args["content"]!.Value<string>()!.Split('\n').Length, "line"));
            }
            if (state == ToolState.Background)
            {
                parts.Clear();
            }
            else if (item.EndedAt.HasValue)
            {
                parts.Add(Format.FormatDuration(item.EndedAt.Value - item.StartedAt));
            }

            if (state == ToolState.Background)
            {
                parts.Add("in background");
            }

            return new ToolHeadline(name, RelativeTo(PrimaryArg(item, kind), cwd), glyph, string.Join(" · ", parts), ErrorLine(item.Result), state);
        }
        catch (Exception error)
        {
            return new ToolHeadline(item.Name, "", glyph, "", $"Could not summarize this call: {error.Message}", state);
        }
    }

    /// <summary><c>path:12</c>, <c>path:12-40</c>, <c>path:5-16,960-973</c>, <c>path:raw</c> read selectors → path + first line.</summary>
    public static FileSelector SplitSelector(string raw)
    {
        var path = raw;
        int? line = null;
        while (true)
        {
            var match = SelectorSuffix.Match(path);
            if (!match.Success || match.Index == 0)
            {
                break;
            }

            if (!line.HasValue)
            {
                var number = LeadingNumber.Match(match.Groups[1].Value);
                if (number.Success && int.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    line = parsed;
                }
            }
            path = path.Substring(0, match.Index);
        }

        return new FileSelector(path, line);
    }

    /// <summary>File a file tool acted on and the most useful line to open it at.</summary>
    public static FileSelector? FileTarget(ToolItem item)
    {
        var args = item.Args as JObject ?? new JObject();
        var details = item.Result?.Details as JObject ?? new JObject();
        var raw = Str(args["path"]) ?? Str(args["file"]) ?? Str(details["path"]) ?? Str(details["resolvedPath"]);
        if (raw is null)
        {
            return null;
        }

        var target = SplitSelector(raw);
        var line = Num(details["firstChangedLine"]) ?? Num(args["line"]) ?? Num(args["offset"]) ?? target.Line;

        return new FileSelector(target.Path, line.HasValue && line.Value > 0 && line.Value <= int.MaxValue ? (int?)line.Value : null);
    }

    /// <summary>
    /// Every file a tool call names, as OMP wrote the path: the <see cref="FileTarget"/> first (with its line), then
    /// the other argument paths (multi-file edits, patches) and result paths, one entry per file resolved against
    /// <paramref name="cwd"/>. A file carries the old text when the tool's result recorded it.
    /// </summary>
    public static IReadOnlyList<ToolFile> Files(ToolItem item, string? cwd)
    {
        var results = ChangePaths.ToolResultFiles(item.Result?.Details);
        var files = new List<ToolFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Identity(string raw) => (string.IsNullOrEmpty(cwd) ? null : ChangePaths.ResolveToolPath(raw, cwd!)) ?? raw;
        string? OldText(string identity) => results.FirstOrDefault(r => r.OldText != null && string.Equals(Identity(r.Path), identity, StringComparison.OrdinalIgnoreCase))?.OldText;
        void Add(string raw, int? line)
        {
            var identity = Identity(raw);
            if (seen.Add(identity))
            {
                files.Add(new ToolFile(raw, line, OldText(identity)));
            }
        }
        var target = FileTarget(item);
        if (target.HasValue)
        {
            Add(target.Value.Path, target.Value.Line);
        }

        foreach (var raw in ChangePaths.ToolArgPaths(item.Args))
        {
            Add(SplitSelector(raw).Path, null);
        }

        foreach (var result in results)
        {
            Add(result.Path, null);
        }

        return files;
    }

    /// <summary>Unified diff from tool details: per-file diffs (multi-file edits) or a single <c>diff</c>.</summary>
    public static string? CollectDiff(JToken? details)
    {
        if (!(details is JObject record))
        {
            return null;
        }

        if (record["perFileResults"] is JArray perFile)
        {
            var diffs = perFile.OfType<JObject>().Where(entry => Str(entry["diff"]) != null).SelectMany(entry => new[] { $"--- {Str(entry["path"]) ?? ""}", Str(entry["diff"])! }).ToList();
            if (diffs.Count > 0)
            {
                return string.Join("\n", diffs);
            }
        }

        return Str(record["diff"]);
    }
}
