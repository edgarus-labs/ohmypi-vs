using System.IO;

namespace OhMyPi.VisualStudio.Logic
{
    internal static class LocalPaths
    {
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        /// <summary>
        /// Whether <paramref name="path"/> is a rooted path of an existing file, or of an existing directory when
        /// <paramref name="allowDirectories"/>; false for document monikers and canonical names that are no paths
        /// (.NET Framework's <see cref="Path.IsPathRooted"/> throws on their characters).
        /// </summary>
        public static bool Exists(string? path, bool allowDirectories) =>
            !string.IsNullOrEmpty(path)
            && path!.IndexOfAny(InvalidPathChars) < 0
            && Path.IsPathRooted(path)
            && (File.Exists(path) || allowDirectories && Directory.Exists(path));
    }
}
