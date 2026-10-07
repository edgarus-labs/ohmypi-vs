using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Omp.Core.Changes;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>What transcript renderers need: remembered expand state, OMP's working directory, tracked changes and host actions.</summary>
    internal sealed class RenderContext
    {
        public RenderContext(OpenState open, Action<string, int?> openFile, Action<string, string?> openDiff, Action showAgents, Action<string> openUrl, Action<string> copy, Action<string, Exception> logError)
        {
            Open = open;
            OpenFile = openFile;
            OpenDiff = openDiff;
            ShowAgents = showAgents;
            OpenUrl = openUrl;
            Copy = copy;
            LogError = logError;
        }

        public OpenState Open { get; }

        /// <summary>OMP working directory; paths under it render relative in collapsed rows.</summary>
        public string? Cwd { get; set; }

        /// <summary>Files OMP changed that the host can diff against a baseline.</summary>
        public IReadOnlyList<TrackedChange> Changes { get; set; } = Array.Empty<TrackedChange>();

        /// <summary>Opens a tool path (raw, as OMP reported it) in the editor at an optional line.</summary>
        public Action<string, int?> OpenFile { get; }

        /// <summary>Opens the native diff of a tool path (raw) with the tool's own record of the old text, when it has one.</summary>
        public Action<string, string?> OpenDiff { get; }

        public Action ShowAgents { get; }
        public Action<string> OpenUrl { get; }
        public Action<string> Copy { get; }

        /// <summary>Writes a rendering failure with its exception to the log.</summary>
        public Action<string, Exception> LogError { get; }

        /// <summary>Whether <paramref name="rawPath"/> (resolved against <see cref="Cwd"/>) is one of <see cref="Changes"/>.</summary>
        public bool IsTracked(string rawPath)
        {
            if (string.IsNullOrEmpty(Cwd)) return false;
            var resolved = ChangePaths.ResolveToolPath(rawPath, Cwd!);
            return resolved != null && Changes.Any(change => string.Equals(change.Path, resolved, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Link behavior for Markdown: file references resolve against <see cref="Cwd"/> and open in the editor.</summary>
        public MarkdownLinks Links => _links ??= new MarkdownLinks(OpenUrl, ResolveFile, OpenFile);

        /// <summary>The existing file <paramref name="raw"/> names, resolved against <see cref="Cwd"/>, or null.</summary>
        public FileTarget? ResolveFile(string raw) => FileLinks.Resolve(raw, Cwd, Exists);

        private MarkdownLinks? _links;
        private readonly Dictionary<string, bool> _probed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Remembers which paths exist and which do not, so re-rendering a streaming message probes the disk once per path.
        /// <see cref="ForgetMissingFiles"/> lets paths that did not exist be probed again.
        /// </summary>
        private bool Exists(string path)
        {
            if (_probed.TryGetValue(path, out var exists)) return exists;
            exists = File.Exists(path);
            _probed[path] = exists;
            return exists;
        }

        /// <summary>Drops remembered misses (OMP may have created those files since).</summary>
        public void ForgetMissingFiles()
        {
            foreach (var missing in _probed.Where(pair => !pair.Value).Select(pair => pair.Key).ToList()) _probed.Remove(missing);
        }
    }
}
