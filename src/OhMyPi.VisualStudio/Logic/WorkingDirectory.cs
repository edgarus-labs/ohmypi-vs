using System;
using System.IO;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>OMP's working directory: the open solution's directory, else the open folder, else the user profile.</summary>
internal static class WorkingDirectory
{
    /// <summary>
    /// Resolves the primary working directory by prioritizing the solution directory, followed by the open folder, and falling back to the user profile path.
    /// </summary>
    /// <param name="solutionDirectory">The solution directory.</param>
    /// <param name="openFolder">The open folder.</param>
    /// <param name="userProfile">The user profile.</param>
    /// <returns>The string result.</returns>
    public static string Resolve(string? solutionDirectory, string? openFolder, string userProfile)
    {
        if (!string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return Normalize(solutionDirectory!);
        }

        if (!string.IsNullOrWhiteSpace(openFolder))
        {
            return Normalize(openFolder!);
        }

        return Normalize(userProfile);
    }

    /// <summary>
    /// Determines whether two strings are equivalent after applying normalization and performing a case-insensitive ordinal comparison.
    /// </summary>
    /// <param name="a">The a.</param>
    /// <param name="b">The b.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes the specified directory path by resolving it to a full path and removing trailing separators, while preserving the root directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The string result.</returns>
    public static string Normalize(string directory)
    {
        var full = Path.GetFullPath(directory.Trim());
        var root = Path.GetPathRoot(full) ?? "";

        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }
}
