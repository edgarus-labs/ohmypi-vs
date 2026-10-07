using System.Collections.Generic;
using System.Text;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>Splits the Extra arguments option the way Windows splits a command line.</summary>
internal static class CommandLine
{
    public static IReadOnlyList<string> Split(string? text)
    {
        var args = new List<string>();
        if (text is null)
        {
            return args;
        }

        var current = new StringBuilder();
        var inArg = false;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (inArg)
                {
                    args.Add(current.ToString());
                }

                current.Clear();
                inArg = false;
                continue;
            }
            inArg = true;
            if (c == '\\')
            {
                var start = i;
                while (i < text.Length && text[i] == '\\')
                {
                    i++;
                }

                var count = i - start;
                if (i < text.Length && text[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        i--;
                    }
                }
                else
                {
                    current.Append('\\', count);
                    i--;
                }
                continue;
            }
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }
            current.Append(c);
        }
        if (inArg)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
