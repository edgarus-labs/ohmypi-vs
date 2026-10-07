using System;
using System.IO;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>OMP's working directory: the open solution's directory, else the open folder, else the user profile.</summary>
internal static class WorkingDirectory
{
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

    public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string directory)
    {
        var full = Path.GetFullPath(directory.Trim());
        var root = Path.GetPathRoot(full) ?? "";

        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }
}
