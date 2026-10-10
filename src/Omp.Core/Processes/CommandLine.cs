using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Omp.Core.Processes;

/// <summary>Windows command lines and environment blocks for <c>CreateProcess</c>.</summary>
internal static class CommandLine
{
    /// <summary>cmd.exe metacharacters; <c>^</c> makes the next one literal.</summary>
    private static readonly Regex CmdMeta = new Regex("([()\\][%!^\"`<>&|;, *?])", RegexOptions.CultureInvariant);
    private static readonly Regex BatchFile = new Regex(@"\.(cmd|bat)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QuotesAfterBackslashes = new Regex("(\\\\*)\"", RegexOptions.CultureInvariant);
    private static readonly Regex TrailingBackslashes = new Regex("(\\\\*)\\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The application to start and its full command line. Windows runs <c>.cmd</c>/<c>.bat</c> launchers (npm installs
    /// <c>omp.cmd</c>) only through cmd.exe, which re-parses the command line, so the path is caret-escaped and every
    /// argument is quoted and caret-escaped twice, because the launcher re-parses its arguments when it expands <c>%*</c>.
    /// </summary>
    public static (string Application, string CommandLine) Build(string executable, IReadOnlyList<string> args, string? comSpec)
    {
        if (BatchFile.IsMatch(executable))
        {
            var cmd = string.IsNullOrEmpty(comSpec) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : comSpec!;
            var line = string.Join(" ", new[] { CmdMeta.Replace(executable, "^$1") }.Concat(args.Select(CmdArgument)));

            return (cmd, $"{QuoteProgram(cmd)} /d /s /c \"{line}\"");
        }

        return (executable, string.Join(" ", new[] { QuoteProgram(executable) }.Concat(args.Select(Quote))));
    }

    /// <summary>One argument quoted for the Microsoft C runtime / <c>CommandLineToArgvW</c> rules.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return arg;
        }

        var quoted = new StringBuilder("\"");
        for (var i = 0; i < arg.Length;)
        {
            var c = arg[i++];
            if (c == '\\')
            {
                var count = 1;
                while (i < arg.Length && arg[i] == '\\')
                {
                    i++;
                    count++;
                }
                if (i == arg.Length)
                {
                    quoted.Append('\\', count * 2);
                }
                else if (arg[i] == '"')
                {
                    quoted.Append('\\', count * 2 + 1).Append('"');
                    i++;
                }
                else
                {
                    quoted.Append('\\', count);
                }
                continue;
            }
            if (c == '"')
            {
                quoted.Append("\\\"");
            }
            else
            {
                quoted.Append(c);
            }
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>
    /// The process environment with <paramref name="overrides"/> applied (a null value removes a variable), as a
    /// sorted, double-NUL-terminated Unicode block.
    /// </summary>
    public static string EnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = (string?)entry.Value ?? "";
        }

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (pair.Value is null)
                {
                    variables.Remove(pair.Key);
                }
                else
                {
                    variables[pair.Key] = pair.Value;
                }
            }
        }
        var block = new StringBuilder();
        foreach (var pair in variables)
        {
            block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }

        return block.Append('\0').ToString();
    }

    /// <summary>
    /// Escapes and wraps a string argument in double quotes to ensure it is properly formatted for command-line execution.
    /// </summary>
    /// <param name="arg">The arg.</param>
    /// <returns>The string result.</returns>
    private static string CmdArgument(string arg)
    {
        var quoted = "\"" + TrailingBackslashes.Replace(QuotesAfterBackslashes.Replace(arg, "$1$1\\\""), "$1$1") + "\"";

        return CmdMeta.Replace(CmdMeta.Replace(quoted, "^$1"), "^$1");
    }

    /// <summary>argv[0] is read up to the closing quote, so it is only wrapped, never escaped.</summary>
    private static string QuoteProgram(string path) => path.IndexOfAny([' ', '\t']) >= 0 ? $"\"{path}\"" : path;
}
