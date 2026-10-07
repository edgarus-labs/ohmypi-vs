using Omp.Core;
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OhMyPi.VisualStudio.Logic;

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
            foreach (var b in hash)
            {
                hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }
    }

    /// <summary>Session to bind when OMP starts without an explicit choice; <paramref name="preferred"/> wins while its file exists.</summary>
    public static StartOptions? ResumeOptions(string? preferred, string? last, Func<string, bool> exists, Action<string>? log)
    {
        if (!string.IsNullOrEmpty(preferred) && exists(preferred!))
        {
            return new StartOptions { ResumeSessionFile = preferred };
        }

        if (string.IsNullOrEmpty(last))
        {
            return null;
        }

        if (exists(last!))
        {
            return new StartOptions { ResumeSessionFile = last };
        }

        log?.Invoke($"Last session file no longer exists: {last}");

        return null;
    }
}
