using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Omp.Core;

namespace OhMyPi.VisualStudio.Logic
{
    public enum StartupMode { ResumeLast, NewSession, Manual }

    public enum OmpLogLevel { Error, Warn, Info, Debug }

    /// <summary>Splits the Extra arguments option the way Windows splits a command line.</summary>
    internal static class CommandLine
    {
        public static IReadOnlyList<string> Split(string? text)
        {
            var args = new List<string>();
            if (text == null) return args;
            var current = new StringBuilder();
            var inArg = false;
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (inArg) args.Add(current.ToString());
                    current.Clear();
                    inArg = false;
                    continue;
                }
                inArg = true;
                if (c == '\\')
                {
                    var start = i;
                    while (i < text.Length && text[i] == '\\') i++;
                    var count = i - start;
                    if (i < text.Length && text[i] == '"')
                    {
                        current.Append('\\', count / 2);
                        if (count % 2 == 1) current.Append('"');
                        else i--;
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
            if (inArg) args.Add(current.ToString());
            return args;
        }
    }

    /// <summary>OMP's working directory: the open solution's directory, else the open folder, else the user profile.</summary>
    internal static class WorkingDirectory
    {
        public static string Resolve(string? solutionDirectory, string? openFolder, string userProfile)
        {
            if (!string.IsNullOrWhiteSpace(solutionDirectory)) return Normalize(solutionDirectory!);
            if (!string.IsNullOrWhiteSpace(openFolder)) return Normalize(openFolder!);
            return Normalize(userProfile);
        }

        public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        public static string Normalize(string directory)
        {
            var full = Path.GetFullPath(directory.Trim());
            var root = Path.GetPathRoot(full) ?? "";
            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }
    }

    /// <summary>The last OMP session file remembered per working directory.</summary>
    internal static class LastSession
    {
        /// <summary>Settings-store key for a working directory (SHA-256 hex of the normalized path).</summary>
        public static string KeyFor(string cwd)
        {
            var normalized = WorkingDirectory.Normalize(cwd).ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>Session to bind when OMP starts without an explicit choice; <paramref name="preferred"/> wins while its file exists.</summary>
        public static StartOptions? ResumeOptions(string? preferred, string? last, Func<string, bool> exists, Action<string>? log)
        {
            if (!string.IsNullOrEmpty(preferred) && exists(preferred!)) return new StartOptions { ResumeSessionFile = preferred };
            if (string.IsNullOrEmpty(last)) return null;
            if (exists(last!)) return new StartOptions { ResumeSessionFile = last };
            log?.Invoke($"Last session file no longer exists: {last}");
            return null;
        }
    }

    internal static class LogFormat
    {
        public static bool Allows(OmpLogLevel configured, OmpLogLevel level) => level <= configured;

        public static string Line(DateTime utc, OmpLogLevel level, string message, Exception? error)
        {
            var text = error == null ? message : $"{message}: {error}";
            return $"{Timestamp(utc)} [{level.ToString().ToLowerInvariant()}] {text}";
        }

        public static string Trace(DateTime utc, string direction, string frame) =>
            $"{Timestamp(utc)} [trace] {(direction == "in" ? "<-" : "->")} {frame}";

        private static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
