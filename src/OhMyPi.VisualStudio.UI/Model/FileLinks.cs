using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>A file a chat message points at, with an optional 1-based line.</summary>
    internal sealed class FileTarget
    {
        public FileTarget(string path, int? line)
        {
            Path = path;
            Line = line;
        }

        public string Path { get; }
        public int? Line { get; }
    }

    /// <summary>Recognizes file references in chat text (code spans and link targets) so they open in the editor.</summary>
    internal static class FileLinks
    {
        /// <summary><c>path</c>, <c>path:12</c>, <c>path:12:3</c>, <c>path#L12</c> or <c>path#L12-L20</c>.</summary>
        private static readonly Regex Reference = new Regex(@"^(?<path>.+?)(?::(?<line>\d+)(?::\d+)?|#L(?<line>\d+)(?:-L?\d+)?)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// The existing file <paramref name="text"/> names (relative paths resolve against <paramref name="cwd"/>), or null.
        /// Network (UNC, <c>file://host</c>) and Win32 device paths are refused before <paramref name="exists"/> runs:
        /// probing them would open an SMB connection on the UI thread for any path a model writes.
        /// </summary>
        public static FileTarget? Resolve(string text, string? cwd, Func<string, bool> exists)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 260 || trimmed.Any(char.IsWhiteSpace)) return null;
            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !uri.IsFile || !string.IsNullOrEmpty(uri.Host)) return null;
                trimmed = uri.LocalPath;
            }
            else if (trimmed.Contains("://")) return null;
            if (IsNetworkOrDevice(trimmed)) return null;

            var match = Reference.Match(trimmed);
            if (!match.Success) return null;
            var raw = match.Groups["path"].Value;
            if (raw.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || raw.IndexOfAny(new[] { '*', '?', '"', '<', '>', '|' }) >= 0) return null;
            if (Path.GetExtension(raw).Length == 0) return null;

            string full;
            try
            {
                if (Path.IsPathRooted(raw)) full = Path.GetFullPath(raw);
                else if (!string.IsNullOrEmpty(cwd)) full = Path.GetFullPath(Path.Combine(cwd, raw));
                else return null;
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
            {
                return null;
            }
            if (IsNetworkOrDevice(full) || !exists(full)) return null;
            int? line = match.Groups["line"].Success && int.TryParse(match.Groups["line"].Value, out var number) && number > 0 ? number : (int?)null;
            return new FileTarget(full, line);
        }

        private static readonly char[] TokenDelimiters = { '\'', '"', '`', '(', ')', '[', ']', '{', '}', '<', '>', '|', ',', ';' };

        /// <summary>
        /// The path-like word of <paramref name="text"/> that covers <paramref name="index"/>: delimited by whitespace,
        /// quotes, brackets, pipes, commas and semicolons, without a trailing period or colon. Null outside any word.
        /// </summary>
        public static string? TokenAt(string text, int index)
        {
            bool IsDelimiter(char c) => char.IsWhiteSpace(c) || Array.IndexOf(TokenDelimiters, c) >= 0;
            if (index < 0 || index >= text.Length || IsDelimiter(text[index])) return null;
            var start = index;
            while (start > 0 && !IsDelimiter(text[start - 1])) start--;
            var end = index;
            while (end < text.Length - 1 && !IsDelimiter(text[end + 1])) end++;
            var token = text.Substring(start, end - start + 1).TrimEnd('.', ':');
            return token.Length == 0 ? null : token;
        }

        /// <summary><c>\\server\share</c>, <c>//server/share</c>, <c>\\?\…</c> and <c>\\.\…</c>.</summary>
        private static bool IsNetworkOrDevice(string path) =>
            path.Length >= 2 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/');
    }
}
