using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Omp.Core;

/// <summary>Locates the <c>omp</c> executable.</summary>
public static class ExecutableResolver
{
    private static readonly Regex Launchable = new Regex(@"\.(exe|com|cmd|bat)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentVariable = new Regex("%([^%]+)%", RegexOptions.CultureInvariant);
    private static readonly string[] Names = ["omp.exe", "omp.cmd"];

    /// <summary>
    /// A configured path must be absolute after <c>~</c> and <c>%VAR%</c> expansion, name a <c>.exe</c>, <c>.com</c>,
    /// <c>.cmd</c> or <c>.bat</c> file, and exist. Otherwise the rooted entries of PATH are searched for <c>omp.exe</c>
    /// and <c>omp.cmd</c>, then <c>~\.local\bin</c>, <c>~\.bun\bin</c> and <c>%LOCALAPPDATA%\omp</c>. Relative PATH
    /// entries are skipped: they would resolve against the current directory of Visual Studio.
    /// </summary>
    /// <exception cref="FileNotFoundException">The message names the rejected configured path or every location tried.</exception>
    public static string Locate(string? configured) =>
        Locate(configured, Environment.GetEnvironmentVariable, File.Exists);

    /// <summary>
    /// Resolves the absolute path to the OMP executable by validating a configured path or searching through system environment variables and predefined installation directories.
    /// </summary>
    /// <param name="configured">The configured.</param>
    /// <param name="env">The env.</param>
    /// <param name="isFile">The is file.</param>
    /// <returns>The string result.</returns>
    /// <exception cref="FileNotFoundException">Thrown when an error occurs during execution.</exception>
    internal static string Locate(string? configured, Func<string, string?> env, Func<string, bool> isFile)
    {
        var home = env("USERPROFILE") ?? env("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var trimmed = configured?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            var expanded = ChangePaths.ExpandHome(EnvironmentVariable.Replace(trimmed, match => env(match.Groups[1].Value) ?? match.Value), home);
            if (!IsAbsolute(expanded))
            {
                throw new FileNotFoundException($"The configured OMP executable path must be absolute or start with ~, but is relative: {expanded}", expanded);
            }

            if (!Launchable.IsMatch(expanded))
            {
                throw new FileNotFoundException($"The configured OMP executable path must name a .exe, .com, .cmd or .bat file: {expanded}", expanded);
            }

            if (isFile(expanded))
            {
                return expanded;
            }

            throw new FileNotFoundException($"The configured OMP executable was not found: {expanded}", expanded);
        }

        var dirs = (env("Path") ?? env("PATH") ?? "").Split(';').Select(d => d.Trim().Trim('"')).Where(IsAbsolute).ToList();
        dirs.Add(Path.Combine(home, ".local", "bin"));
        dirs.Add(Path.Combine(home, ".bun", "bin"));
        var localAppData = env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(localAppData))
        {
            dirs.Add(Path.Combine(localAppData, "omp"));
        }

        var tried = new List<string>();
        foreach (var dir in dirs)
        {
            foreach (var name in Names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (isFile(candidate))
                {
                    return candidate;
                }

                tried.Add(candidate);
            }
        }

        throw new FileNotFoundException(
            "Could not find the omp executable. Install oh-my-pi or configure the OMP executable path. Tried:\n" +
            string.Join("\n", tried.Select(p => "  " + p)));
    }

    /// <summary>
    /// Determines whether the specified path is an absolute path, supporting both UNC and drive-letter formats.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    private static bool IsAbsolute(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'));
}
