using Omp.Core.Changes;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Changes section rows: title, status letter, file name and folder detail.</summary>
internal static class ChangeRows
{
    /// <summary>
    /// Returns a formatted string representing the number of files, handling singular and plural forms based on the specified count.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <returns>The string result.</returns>
    public static string Summary(int count) => count == 0 ? "" : $"{count} {(count == 1 ? "file" : "files")}";

    /// <summary>
    /// Returns a formatted title string indicating the number of changes.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <returns>The string result.</returns>
    public static string Title(int count) => count == 0 ? "Changes" : $"Changes · {Summary(count)}";

    /// <summary>
    /// Converts a change status enumeration value into its corresponding single-character string representation.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The string result.</returns>
    public static string StatusLetter(ChangeStatus status) =>
        status == ChangeStatus.Added ? "A" : status == ChangeStatus.Deleted ? "D" : "M";

    /// <summary>
    /// Converts a specified change status into its corresponding human-readable string representation.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The string result.</returns>
    public static string StatusText(ChangeStatus status) =>
        status == ChangeStatus.Added ? "added" : status == ChangeStatus.Deleted ? "deleted" : "modified";

    /// <summary>
    /// Extracts the file name from the path of the specified tracked change.
    /// </summary>
    /// <param name="change">The change.</param>
    /// <returns>The string result.</returns>
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

    /// <summary>
    /// Calculates the relative path of a directory based on the provided current working directory.
    /// </summary>
    /// <param name="dir">The dir.</param>
    /// <param name="cwd">The cwd.</param>
    /// <returns>The string result.</returns>
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
