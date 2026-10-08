using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model;

internal enum CodeTokenKind { Text, Comment, String, Number, Keyword, Key, Tag }

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

/// <summary>
/// Lexical coloring of code for reading: comments, strings, numbers, keywords, JSON and YAML keys and XML tags, by
/// language family. It never parses, so cut or invalid code still colors; an unknown language gets strings and
/// numbers only, and the tokens always cover the whole text in order.
/// </summary>
internal static class CodeHighlighter
{
    private sealed class Syntax
    {
        public string? LineComment;
        public (string Open, string Close)? BlockComment;
        public string Quotes = "\"'";
        public bool TripleQuotes;
        public bool BacktickEscapes;
        public bool JsonKeys;
        public bool YamlKeys;
        public bool Xml;
        public bool IgnoreCase;
        public bool Commands;
        public HashSet<string> Openers = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal);
    }

    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp", ["c#"] = "csharp", ["csharp"] = "csharp",
        ["js"] = "javascript", ["jsx"] = "javascript", ["javascript"] = "javascript", ["mjs"] = "javascript",
        ["ts"] = "typescript", ["tsx"] = "typescript", ["typescript"] = "typescript",
        ["py"] = "python", ["python"] = "python",
        ["sh"] = "bash", ["bash"] = "bash", ["zsh"] = "bash", ["shell"] = "bash", ["console"] = "bash",
        ["ps"] = "powershell", ["ps1"] = "powershell", ["pwsh"] = "powershell", ["powershell"] = "powershell",
        ["json"] = "json", ["jsonc"] = "json", ["json5"] = "json",
        ["xml"] = "xml", ["html"] = "xml", ["xaml"] = "xml", ["csproj"] = "xml", ["props"] = "xml", ["svg"] = "xml", ["razor"] = "xml", ["cshtml"] = "xml", ["vsct"] = "xml", ["slnx"] = "xml",
        ["yaml"] = "yaml", ["yml"] = "yaml",
        ["sql"] = "sql",
        ["go"] = "go", ["golang"] = "go",
        ["rs"] = "rust", ["rust"] = "rust",
        ["c"] = "c", ["h"] = "c", ["cpp"] = "c", ["hpp"] = "c", ["cc"] = "c", ["java"] = "c", ["kt"] = "c", ["kotlin"] = "c", ["swift"] = "c", ["php"] = "c",
        ["ini"] = "ini", ["toml"] = "ini", ["env"] = "ini", ["properties"] = "ini", ["editorconfig"] = "ini", ["gitignore"] = "bash", ["dockerfile"] = "bash", ["makefile"] = "bash", ["bat"] = "bash", ["cmd"] = "bash",
        ["css"] = "c", ["scss"] = "c", ["less"] = "c", ["dart"] = "c", ["scala"] = "c", ["groovy"] = "c", ["gradle"] = "c", ["proto"] = "c", ["graphql"] = "c", ["gql"] = "c",
        ["rb"] = "python", ["ruby"] = "python", ["lua"] = "python", ["r"] = "python", ["tf"] = "python", ["hcl"] = "python", ["pl"] = "python", ["perl"] = "python",
        ["vue"] = "xml", ["svelte"] = "xml", ["htm"] = "xml", ["xsd"] = "xml", ["xslt"] = "xml", ["plist"] = "xml", ["nuspec"] = "xml", ["targets"] = "xml", ["resx"] = "xml", ["config"] = "xml", ["axaml"] = "xml",
    };

    private static readonly HashSet<string> Plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "txt", "plain", "plaintext", "md", "markdown", "diff", "patch", "log", "output" };

    private static readonly Dictionary<string, Syntax> Syntaxes = new Dictionary<string, Syntax>(StringComparer.Ordinal)
    {
        ["csharp"] = CLike("abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double dynamic else enum event explicit extern false finally fixed float for foreach get goto if implicit in init int interface internal is lock long nameof namespace new null object operator out override params partial private protected public readonly record ref return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using value var virtual void volatile when where while with yield"),
        ["javascript"] = CLike("async await break case catch class const continue debugger default delete do else export extends false finally for from function if import in instanceof let new null of return static super switch this throw true try typeof undefined var void while with yield", "\"'`"),
        ["typescript"] = CLike("abstract any as async await boolean break case catch class const continue debugger declare default delete do else enum export extends false finally for from function if implements import in instanceof interface is keyof let namespace never new null number of override private protected public readonly return satisfies static string super switch this throw true try type typeof undefined unknown var void while with yield", "\"'`"),
        ["c"] = CLike("auto bool boolean break case catch char class const continue default define do double else enum extends extern false final finally float for fun goto if implements import include inline int interface long namespace new null nullptr override package private protected public return short signed sizeof static struct super switch template this throw throws true try typedef typename union unsigned using val var virtual void volatile while"),
        ["go"] = CLike("break case chan const continue default defer else fallthrough false for func go goto if import interface map nil package range return select struct switch true type var", "\"'`"),
        ["rust"] = CLike("as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while"),
        ["python"] = new Syntax { LineComment = "#", TripleQuotes = true, Keywords = Words("False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield self") },
        ["bash"] = new Syntax { LineComment = "#", Quotes = "\"'`", Commands = true, Openers = Words("if then else elif do while until time sudo exec nohup env xargs"), Keywords = Words("alias case cd declare do done echo elif else esac exit export fi for function if in local readonly return select set source then time unset until while") },
        ["powershell"] = new Syntax { LineComment = "#", BlockComment = ("<#", "#>"), BacktickEscapes = true, IgnoreCase = true, Commands = true, Keywords = Words("begin break catch class continue do dynamicparam else elseif end enum filter finally for foreach from function if in param process return switch throw trap try until using while", ignoreCase: true) },
        ["sql"] = new Syntax { LineComment = "--", BlockComment = ("/*", "*/"), IgnoreCase = true, Keywords = Words("add all alter and as asc begin between by case check column commit constraint count create cross default delete desc distinct drop else end exists foreign from full group having in index inner insert into is join key left like limit not null offset on or order outer primary references right rollback select set table then top transaction union unique update values view when where with", ignoreCase: true) },
        ["json"] = new Syntax { Quotes = "\"'", JsonKeys = true, Keywords = Words("true false null") },
        ["yaml"] = new Syntax { LineComment = "#", YamlKeys = true, Keywords = Words("true false null yes no on off ~") },
        ["ini"] = new Syntax { LineComment = "#", YamlKeys = true, Keywords = Words("true false") },
        ["xml"] = new Syntax { BlockComment = ("<!--", "-->"), Xml = true },
    };

    private static Syntax CLike(string keywords, string quotes = "\"'") =>
        new Syntax { LineComment = "//", BlockComment = ("/*", "*/"), Quotes = quotes, Keywords = Words(keywords) };

    private static HashSet<string> Words(string words, bool ignoreCase = false) =>
        new HashSet<string>(words.Split(' '), ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The language family <paramref name="language"/> (a fence info string or file extension) belongs to, or null when it is unknown or plain text.</summary>
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var word = language!.Trim();
        var space = word.IndexOfAny(new[] { ' ', '\t', '{', ':' });
        if (space > 0)
        {
            word = word.Substring(0, space);
        }

        return Plain.Contains(word) ? null : Aliases.TryGetValue(word, out var family) ? family : null;
    }

    /// <summary>
    /// The language of unlabeled code: JSON for an object or array, XML for a tag, else the family whose telltale
    /// words (a using or namespace line, def/import, const/function, func/package) the text starts with; null otherwise.
    /// </summary>
    public static string? GuessLanguage(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '{' || trimmed[0] == '[')
        {
            return "json";
        }

        if (trimmed[0] == '<')
        {
            return "xml";
        }

        foreach (var (pattern, language) in Telltales)
        {
            if (pattern.IsMatch(trimmed))
            {
                return language;
            }
        }

        return null;
    }

    private static readonly (Regex Pattern, string Language)[] Telltales =
    {
        (new Regex(@"^\s*(using\s+[\w.]+;|namespace\s+[\w.]+|(public|internal|private|protected)\s+(static\s+|sealed\s+|abstract\s+|async\s+|override\s+|virtual\s+|readonly\s+)*(class|interface|record|struct|enum|void|var|[A-Z]\w*(<[^>]*>)?(\[\])?)\s)", RegexOptions.Multiline | RegexOptions.Compiled), "csharp"),
        (new Regex(@"^\s*(def\s+\w+\(|class\s+\w+(\(.*\))?:|import\s+[\w.]+\s*$|from\s+[\w.]+\s+import\s)", RegexOptions.Multiline | RegexOptions.Compiled), "python"),
        (new Regex(@"^\s*(const\s+\w+\s*=|let\s+\w+\s*=|function\s+\w*\s*\(|export\s+(default\s+|const\s+|function\s+|class\s+)|import\s+.*\sfrom\s+['""])", RegexOptions.Multiline | RegexOptions.Compiled), "javascript"),
        (new Regex(@"^\s*(package\s+\w+$|func\s+(\(\w+\s+\*?\w+\)\s+)?\w+\()", RegexOptions.Multiline | RegexOptions.Compiled), "go"),
    };

    /// <summary>The tokens of <paramref name="code"/> in <paramref name="language"/> (any alias; see <see cref="Normalize"/>), covering it whole and in order.</summary>
    public static IReadOnlyList<CodeToken> Tokens(string code, string? language)
    {
        var tokens = new List<CodeToken>();
        if (code.Length == 0)
        {
            return tokens;
        }

        var family = Normalize(language);
        var syntax = family is not null && Syntaxes.TryGetValue(family, out var known) ? known : new Syntax();
        new Lexer(code, syntax, tokens).Run();

        return tokens;
    }

    private sealed class Lexer
    {
        private readonly string _s;
        private readonly Syntax _x;
        private readonly List<CodeToken> _out;
        private int _i;
        private int _textStart;
        private bool _inTag;
        private bool _commandNext = true;

        public Lexer(string code, Syntax syntax, List<CodeToken> output)
        {
            _s = code;
            _x = syntax;
            _out = output;
        }

        public void Run()
        {
            while (_i < _s.Length)
            {
                if (_x.Xml)
                {
                    XmlStep();
                }
                else
                {
                    Step();
                }
            }

            FlushText(_s.Length);
        }

        private void Step()
        {
            var c = _s[_i];
            var command = _commandNext;
            if (_x.Commands)
            {
                _commandNext = c == '\n' || c == ';' || c == '|' || c == '&' || c == '(' || c == '{' || c == '`' || (command && char.IsWhiteSpace(c));
                if (c == '$' && _i + 1 < _s.Length && (IsWordChar(_s[_i + 1]) || "{_?@#!*".IndexOf(_s[_i + 1]) >= 0))
                {
                    Emit(VariableEnd(), CodeTokenKind.Key);

                    return;
                }

                if (command && (IsWordStart(c) || c == '.' || c == '/' || c == '~' || c == '\\'))
                {
                    var end = _i + 1;
                    while (end < _s.Length && (IsWordChar(_s[end]) || "-./\\:~+".IndexOf(_s[end]) >= 0))
                    {
                        end++;
                    }

                    _commandNext = _x.Openers.Contains(_s.Substring(_i, end - _i));
                    Emit(end, CodeTokenKind.Keyword);

                    return;
                }
            }

            if (_x.BlockComment.HasValue && At(_x.BlockComment.Value.Open))
            {
                var end = _s.IndexOf(_x.BlockComment.Value.Close, _i + _x.BlockComment.Value.Open.Length, StringComparison.Ordinal);
                Emit(end < 0 ? _s.Length : end + _x.BlockComment.Value.Close.Length, CodeTokenKind.Comment);

                return;
            }

            if (_x.LineComment is not null && At(_x.LineComment))
            {
                Emit(LineEnd(), CodeTokenKind.Comment);

                return;
            }

            if (_x.Quotes.IndexOf(c) >= 0)
            {
                var end = StringEnd();
                Emit(end, _x.JsonKeys && NextNonSpace(end) == ':' ? CodeTokenKind.Key : CodeTokenKind.String);

                return;
            }

            if (char.IsDigit(c) || (c == '-' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1]) && !IsWordChar(Prev())))
            {
                if (IsWordChar(Prev()))
                {
                    _i++;

                    return;
                }

                var end = _i + 1;
                while (end < _s.Length && (char.IsLetterOrDigit(_s[end]) || _s[end] == '.' || _s[end] == '_'))
                {
                    end++;
                }

                Emit(end, CodeTokenKind.Number);

                return;
            }

            if (IsWordStart(c))
            {
                var end = _i + 1;
                while (end < _s.Length && IsWordChar(_s[end]))
                {
                    end++;
                }

                var word = _s.Substring(_i, end - _i);
                if (_x.YamlKeys && AtLineStart() && NextNonSpace(end) == ':')
                {
                    Emit(end, CodeTokenKind.Key);
                }
                else if (_x.Keywords.Contains(word))
                {
                    Emit(end, CodeTokenKind.Keyword);
                }
                else
                {
                    _i = end;
                }

                return;
            }

            _i++;
        }

        private void XmlStep()
        {
            var c = _s[_i];
            if (!_inTag && At("<!--"))
            {
                var end = _s.IndexOf("-->", _i + 4, StringComparison.Ordinal);
                Emit(end < 0 ? _s.Length : end + 3, CodeTokenKind.Comment);

                return;
            }

            if (!_inTag)
            {
                if (c == '<')
                {
                    var end = _i + 1;
                    while (end < _s.Length && (_s[end] == '/' || _s[end] == '?' || _s[end] == '!'))
                    {
                        end++;
                    }

                    while (end < _s.Length && (IsWordChar(_s[end]) || _s[end] == ':' || _s[end] == '-' || _s[end] == '.'))
                    {
                        end++;
                    }

                    Emit(end, CodeTokenKind.Tag);
                    _inTag = true;

                    return;
                }

                _i++;

                return;
            }

            if (c == '>' || At("/>") || At("?>"))
            {
                Emit(c == '>' ? _i + 1 : _i + 2, CodeTokenKind.Tag);
                _inTag = false;

                return;
            }

            if (c == '"' || c == '\'')
            {
                Emit(StringEnd(), CodeTokenKind.String);

                return;
            }

            if (IsWordStart(c))
            {
                var end = _i + 1;
                while (end < _s.Length && (IsWordChar(_s[end]) || _s[end] == ':' || _s[end] == '-' || _s[end] == '.'))
                {
                    end++;
                }

                Emit(end, CodeTokenKind.Key);

                return;
            }

            _i++;
        }

        /// <summary>Index just past the string starting at the current quote: its matching quote (escapes skipped), the line end for a single-line quote, or the text end.</summary>
        private int StringEnd()
        {
            var quote = _s[_i];
            var triple = _x.TripleQuotes && At(new string(quote, 3));
            var j = _i + (triple ? 3 : 1);
            while (j < _s.Length)
            {
                var c = _s[j];
                if (c == (_x.BacktickEscapes ? '`' : '\\') && j + 1 < _s.Length)
                {
                    j += 2;
                    continue;
                }

                if (triple)
                {
                    if (string.CompareOrdinal(_s, j, new string(quote, 3), 0, 3) == 0)
                    {
                        return j + 3;
                    }
                }
                else if (c == quote)
                {
                    return j + 1;
                }
                else if (c == '\n')
                {
                    return j;
                }

                j++;
            }

            return _s.Length;
        }

        /// <summary>Index just past the shell variable at the current <c>$</c>: a braced name to its brace, else the name with a scope prefix like <c>env:</c>.</summary>
        private int VariableEnd()
        {
            if (_s[_i + 1] == '{')
            {
                var close = _s.IndexOf('}', _i + 2);

                return close < 0 ? LineEnd() : close + 1;
            }

            var j = _i + 2;
            while (j < _s.Length && (IsWordChar(_s[j]) || (_s[j] == ':' && j + 1 < _s.Length && IsWordChar(_s[j + 1]))))
            {
                j++;
            }

            return j;
        }

        private bool At(string what) => string.CompareOrdinal(_s, _i, what, 0, what.Length) == 0;

        private int LineEnd()
        {
            var end = _s.IndexOf('\n', _i);

            return end < 0 ? _s.Length : end;
        }

        private char Prev() => _i == 0 ? ' ' : _s[_i - 1];

        private bool AtLineStart()
        {
            for (var k = _i - 1; k >= 0 && _s[k] != '\n'; k--)
            {
                if (_s[k] != ' ' && _s[k] != '\t' && _s[k] != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private int NextNonSpaceIndex(int from)
        {
            while (from < _s.Length && (_s[from] == ' ' || _s[from] == '\t'))
            {
                from++;
            }

            return from;
        }

        private char NextNonSpace(int from)
        {
            var k = NextNonSpaceIndex(from);

            return k < _s.Length ? _s[k] : '\0';
        }

        private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_' || c == '$' || c == '@';

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private void Emit(int end, CodeTokenKind kind)
        {
            FlushText(_i);
            _out.Add(new CodeToken(_i, end - _i, kind));
            _i = end;
            _textStart = end;
        }

        private void FlushText(int upTo)
        {
            if (upTo > _textStart)
            {
                _out.Add(new CodeToken(_textStart, upTo - _textStart, CodeTokenKind.Text));
            }

            _textStart = upTo;
        }
    }
}
