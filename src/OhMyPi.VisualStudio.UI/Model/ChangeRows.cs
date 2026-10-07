using Omp.Core.Changes;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Changes section rows: title, status letter, file name and folder detail.</summary>
internal static class ChangeRows
{
    public static string Summary(int count) => count == 0 ? "" : $"{count} {(count == 1 ? "file" : "files")}";

    public static string Title(int count) => count == 0 ? "Changes" : $"Changes · {Summary(count)}";

    public static string StatusLetter(ChangeStatus status) =>
        status == ChangeStatus.Added ? "A" : status == ChangeStatus.Deleted ? "D" : "M";

    public static string StatusText(ChangeStatus status) =>
        status == ChangeStatus.Added ? "added" : status == ChangeStatus.Deleted ? "deleted" : "modified";

    public static string FileName(TrackedChange change) => System.IO.Path.GetFileName(change.Path);

    /// <summary><c>src\auth · +3 −1</c>: the folder relative to <paramref name="cwd"/> when inside it, and the line counts.</summary>
    public static string Detail(TrackedChange change, string? cwd)
    {
        var dir = System.IO.Path.GetDirectoryName(change.Path) ?? "";
        var shown = cwd is null ? dir : RelativeDir(dir, cwd);
        var parts = new List<string>();
        if (shown.Length > 0 && shown != ".")
        {
            parts.Add(shown);
        }

        if (change.Status != ChangeStatus.Deleted)
        {
            parts.Add($"+{change.Added} −{change.Removed}");
        }

        return string.Join(" · ", parts);
    }

    private static string RelativeDir(string dir, string cwd)
    {
        var root = cwd.TrimEnd('\\', '/');
        if (string.Equals(dir.TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return dir.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || dir.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
            ? dir.Substring(root.Length + 1)
            : dir;
    }
}
