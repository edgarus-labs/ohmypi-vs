using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OhMyPi.VisualStudio.Logic
{
    /// <summary>
    /// Read-only copies of the left sides of diff windows, under one root per Visual Studio process
    /// (<c>&lt;parent&gt;\&lt;pid&gt;-&lt;id&gt;</c>) and one batch directory per set of changes, so clearing the changes
    /// deletes exactly the copies its diff windows showed. Identical copies are written once.
    /// </summary>
    internal sealed class DiffBaselines
    {
        private readonly object _gate = new object();
        private int _batch = 1;

        public DiffBaselines(string parent, int processId)
        {
            Root = Path.Combine(parent, processId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        }

        public string Root { get; }

        /// <summary>The batch new copies go to; <see cref="Rotate"/> starts the next one.</summary>
        public int Batch
        {
            get { lock (_gate) return _batch; }
        }

        public string BatchDirectory(int batch) => Path.Combine(Root, batch.ToString(CultureInfo.InvariantCulture));

        /// <summary>A read-only file named like <paramref name="path"/> holding <paramref name="content"/>, in <paramref name="batch"/>.</summary>
        public string Write(int batch, string path, string content)
        {
            var directory = Path.Combine(BatchDirectory(batch), Fingerprint(path, content));
            var file = Path.Combine(directory, Path.GetFileName(path));
            if (File.Exists(file)) return file;
            Directory.CreateDirectory(directory);
            File.WriteAllText(file, content);
            File.SetAttributes(file, FileAttributes.ReadOnly);
            return file;
        }

        /// <summary>Starts a new batch; returns the directory of the previous one, to delete once its diff windows are closed.</summary>
        public string Rotate()
        {
            lock (_gate) return BatchDirectory(_batch++);
        }

        /// <summary>Deletes <paramref name="directory"/> with its read-only copies; a missing directory is fine.</summary>
        /// <exception cref="IOException">A copy is still open.</exception>
        /// <exception cref="UnauthorizedAccessException">A copy cannot be deleted.</exception>
        public static void Delete(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }

        /// <summary>
        /// Deletes the roots under <paramref name="parent"/> that Visual Studio processes no longer running left behind
        /// (a crash skips the cleanup); returns the roots that could not be deleted.
        /// </summary>
        public static IReadOnlyList<(string Root, Exception Error)> SweepStale(string parent, Func<int, bool> isRunning)
        {
            var failures = new List<(string, Exception)>();
            if (!Directory.Exists(parent)) return failures;
            foreach (var root in Directory.EnumerateDirectories(parent))
            {
                var name = Path.GetFileName(root);
                var dash = name.IndexOf('-');
                if (dash <= 0 || !int.TryParse(name.Substring(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var processId)) continue;
                if (isRunning(processId)) continue;
                try
                {
                    Delete(root);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    failures.Add((root, error));
                }
            }
            return failures;
        }

        private static string Fingerprint(string path, string content)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToUpperInvariant() + "\0" + content));
                var text = new StringBuilder(16);
                for (var i = 0; i < 8; i++) text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }
    }
}
