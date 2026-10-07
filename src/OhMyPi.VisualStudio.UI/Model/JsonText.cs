using System;
using System.Text;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Re-indents single-line JSON objects and arrays for reading. The text is validated against RFC 8259 first and every
/// string and number is copied from the source verbatim, so nothing is decoded, normalized or reordered.
/// </summary>
internal static class JsonText
{
    private const int MaxChars = 1_000_000;
    private const int MaxDepth = 64;
    private const string Indent = "  ";

    /// <summary>The indented form of <paramref name="text"/>, or null when it is not one line of valid JSON holding an object or array.</summary>
    public static string? Pretty(string text)
    {
        var source = text.Trim();
        if (source.Length < 2 || source.Length > MaxChars || (source[0] != '{' && source[0] != '[') || source.IndexOf('\n') >= 0 || source.IndexOf('\r') >= 0)
        {
            return null;
        }

        var writer = new Writer(source);

        return writer.TryWrite() ? writer.Result : null;
    }

    private sealed class Writer
    {
        private readonly string _s;
        private readonly StringBuilder _out = new StringBuilder();
        private int _i;

        public Writer(string source) => _s = source;

        public string Result => _out.ToString();

        public bool TryWrite()
        {
            if (!Value(0))
            {
                return false;
            }

            SkipSpace();

            return _i == _s.Length;
        }

        private void SkipSpace()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t'))
            {
                _i++;
            }
        }

        private void Break(int depth)
        {
            _out.Append('\n');
            for (var d = 0; d < depth; d++)
            {
                _out.Append(Indent);
            }
        }

        private bool Value(int depth)
        {
            if (depth > MaxDepth)
            {
                return false;
            }

            SkipSpace();
            if (_i >= _s.Length)
            {
                return false;
            }

            switch (_s[_i])
            {
                case '{': return Container(depth, '{', '}', member: true);
                case '[': return Container(depth, '[', ']', member: false);
                case '"': return String();
                case 't': return Literal("true");
                case 'f': return Literal("false");
                case 'n': return Literal("null");
                default: return Number();
            }
        }

        private bool Container(int depth, char open, char close, bool member)
        {
            _out.Append(open);
            _i++;
            SkipSpace();
            if (_i < _s.Length && _s[_i] == close)
            {
                _i++;
                _out.Append(close);

                return true;
            }
            while (true)
            {
                Break(depth + 1);
                if (member)
                {
                    SkipSpace();
                    if (_i >= _s.Length || _s[_i] != '"' || !String())
                    {
                        return false;
                    }

                    SkipSpace();
                    if (_i >= _s.Length || _s[_i] != ':')
                    {
                        return false;
                    }

                    _i++;
                    _out.Append(": ");
                }
                if (!Value(depth + 1))
                {
                    return false;
                }

                SkipSpace();
                if (_i >= _s.Length)
                {
                    return false;
                }

                if (_s[_i] == ',')
                {
                    _i++;
                    _out.Append(',');
                    continue;
                }
                if (_s[_i] != close)
                {
                    return false;
                }

                _i++;
                Break(depth);
                _out.Append(close);

                return true;
            }
        }

        private bool Literal(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
            {
                return false;
            }

            _out.Append(word);
            _i += word.Length;

            return true;
        }

        private bool String()
        {
            var start = _i++;
            while (_i < _s.Length)
            {
                var c = _s[_i];
                if (c == '"')
                {
                    _i++;
                    _out.Append(_s, start, _i - start);

                    return true;
                }
                if (c < 0x20)
                {
                    return false;
                }

                if (c != '\\')
                {
                    _i++;
                    continue;
                }
                if (_i + 1 >= _s.Length)
                {
                    return false;
                }

                var escape = _s[_i + 1];
                if (escape == 'u')
                {
                    if (_i + 5 >= _s.Length)
                    {
                        return false;
                    }

                    for (var k = 2; k <= 5; k++)
                    {
                        if (Uri.IsHexDigit(_s[_i + k]) == false)
                        {
                            return false;
                        }
                    }
                    _i += 6;
                }
                else if ("\"\\/bfnrt".IndexOf(escape) >= 0)
                {
                    _i += 2;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        private bool Number()
        {
            var start = _i;
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
            }

            if (_i >= _s.Length)
            {
                return false;
            }

            if (_s[_i] == '0')
            {
                _i++;
            }
            else if (!Digits())
            {
                return false;
            }

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                if (!Digits())
                {
                    return false;
                }
            }
            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                {
                    _i++;
                }

                if (!Digits())
                {
                    return false;
                }
            }
            _out.Append(_s, start, _i - start);

            return true;
        }

        private bool Digits()
        {
            var start = _i;
            while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9')
            {
                _i++;
            }

            return _i > start;
        }
    }
}
