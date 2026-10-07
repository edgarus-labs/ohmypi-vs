using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Omp.Core.Changes;

/// <summary>Path extraction and line counting for change tracking.</summary>
public static class ChangePaths
{
    private static readonly Regex SchemeUrl = new Regex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PatchHeader = new Regex(@"^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex DevicePath = new Regex(@"^[\\/]{2}[?.][\\/]", RegexOptions.CultureInvariant);

    /// <summary>Absolute filesystem path for a tool path argument; null for URLs/internal schemes.</summary>
    public static string? ResolveToolPath(string raw, string cwd)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || SchemeUrl.IsMatch(trimmed))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(cwd, ExpandHome(trimmed, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
        }
        catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
        {
            return null;
        }
    }

    /// <summary><c>~</c>, <c>~/rest</c> or <c>~\rest</c> resolved under <paramref name="home"/>; any other path is returned unchanged.</summary>
    public static string ExpandHome(string raw, string home)
    {
        if (raw == "~")
        {
            return home;
        }

        if (raw.StartsWith("~/", StringComparison.Ordinal) || raw.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(home, raw.Substring(2));
        }

        return raw;
    }

    /// <summary>Whether the absolute <paramref name="filePath"/> is one of <paramref name="roots"/> or below one (case-insensitive). Win32 device paths never are.</summary>
    public static bool IsWithinRoots(string filePath, IReadOnlyList<string> roots)
    {
        if (DevicePath.IsMatch(filePath))
        {
            return false;
        }

        var file = Normalize(filePath);
        if (file is null)
        {
            return false;
        }

        foreach (var rawRoot in roots)
        {
            var root = Normalize(rawRoot);
            if (root is null)
            {
                continue;
            }

            if (string.Equals(file, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = root.EndsWith("\\", StringComparison.Ordinal) ? root : root + "\\";
            if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Raw path strings named by tool arguments (not yet resolved).</summary>
    public static IReadOnlyList<string> ToolArgPaths(JToken? args)
    {
        if (!(args is JObject record))
        {
            return Array.Empty<string>();
        }

        var found = new List<string>();
        void Add(JToken? value)
        {
            var text = Json.Str(value)?.Trim();
            if (!string.IsNullOrEmpty(text) && !found.Contains(text!))
            {
                found.Add(text!);
            }
        }
        Add(record["path"]);
        Add(record["file"]);
        if (record["paths"] is JArray paths)
        {
            foreach (var path in paths)
            {
                Add(path);
            }
        }

        Add(record["oldPath"]);
        Add(record["newPath"]);
        if (record["edits"] is JArray edits)
        {
            foreach (var edit in edits)
            {
                if (edit is JObject)
                {
                    Add(edit["path"]);
                }
            }
        }

        var input = Json.Str(record["input"]);
        if (input is not null)
        {
            foreach (Match match in PatchHeader.Matches(input))
            {
                Add(new JValue(match.Groups[1].Value.TrimEnd('\r')));
            }
        }

        return found;
    }

    /// <summary>Files reported by a tool result's <c>details</c> (edit/write/ast_edit shapes).</summary>
    public static IReadOnlyList<ResultFile> ToolResultFiles(JToken? details)
    {
        if (!(details is JObject record))
        {
            return Array.Empty<ResultFile>();
        }

        var files = new List<ResultFile>();
        void Add(JToken? value, JToken? oldText = null)
        {
            var path = Json.Str(value);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var text = Json.Str(oldText);
            var existing = files.FirstOrDefault(f => f.Path == path);
            if (existing is not null)
            {
                if (existing.OldText is null && text is not null)
                {
                    existing.OldText = text;
                }

                return;
            }
            files.Add(new ResultFile(path!, text));
        }
        Add(record["path"], record["oldText"]);
        Add(record["resolvedPath"]);
        if (record["perFileResults"] is JArray results)
        {
            var sources = new List<JToken?>();
            foreach (var entry in results)
            {
                if (!(entry is JObject item))
                {
                    continue;
                }

                Add(item["path"], item["oldText"]);
                sources.Add(item["sourcePath"]);
            }
            foreach (var source in sources)
            {
                Add(source);
            }
        }
        Add(record["sourcePath"]);

        return files;
    }

    /// <summary>Approximate <c>+a -d</c> line counts (multiset difference; ignores moves).</summary>
    public static (int Added, int Removed) LineDelta(string? before, string? after)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Lines(before))
        {
            counts[line] = counts.TryGetValue(line, out var n) ? n + 1 : 1;
        }

        var added = 0;
        foreach (var line in Lines(after))
        {
            if (counts.TryGetValue(line, out var remaining) && remaining > 0)
            {
                counts[line] = remaining - 1;
            }
            else
            {
                added++;
            }
        }

        return (added, counts.Values.Sum());
    }

    private static IEnumerable<string> Lines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var parts = Regex.Split(text, "\r?\n");

        return parts[parts.Length - 1].Length == 0 ? parts.Take(parts.Length - 1) : parts;
    }

    /// <summary>The full path without a trailing separator (a root keeps its own); null when the path is invalid.</summary>
    internal static string? Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";

            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }
        catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
        {
            return null;
        }
    }
}
