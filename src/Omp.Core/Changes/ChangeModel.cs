using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Omp.Core.Changes
{
    public enum ChangeStatus { Added, Modified, Deleted }

    public sealed class TrackedChange
    {
        /// <summary>Absolute filesystem path.</summary>
        public string Path { get; set; } = "";
        public ChangeStatus Status { get; set; }
        public int Added { get; set; }
        public int Removed { get; set; }
    }

    /// <summary>File content at one moment: <see cref="Missing"/> when the file did not exist. A null snapshot means untrackable.</summary>
    public sealed class Snapshot
    {
        public static readonly Snapshot Missing = new Snapshot(false, null);

        private Snapshot(bool exists, string? content)
        {
            Exists = exists;
            Content = content;
        }

        public static Snapshot Of(string content) => new Snapshot(true, content ?? throw new ArgumentNullException(nameof(content)));

        public bool Exists { get; }

        public string? Content { get; }
    }

    /// <summary>Where tool paths resolve (<see cref="Cwd"/>) and which directories may be tracked (<see cref="Roots"/>).</summary>
    public sealed class ChangeScope
    {
        public string Cwd { get; set; } = "";
        public IReadOnlyList<string> Roots { get; set; } = Array.Empty<string>();
    }

    /// <summary>What changed in the model after one tool event.</summary>
    public sealed class ApplyResult
    {
        /// <summary>The set of tracked changes or one of their rows changed.</summary>
        public bool ChangesChanged { get; set; }
    }

    /// <summary>
    /// Baselines and changes of files touched by OMP tools: the first-seen content of each file and how the
    /// file on disk differs from it now. Thread-safe; <see cref="ApplyAsync"/> calls run one at a time in call order.
    /// </summary>
    public sealed class ChangeModel
    {
        internal const int MaxSnapshotBytes = 5 * 1024 * 1024;
        /// <summary>Total baseline text kept per session.</summary>
        private const long MaxBaselineChars = 64L * 1024 * 1024;

        private static readonly HashSet<string> ReadOnlyTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "read", "grep", "glob", "ast_grep", "find", "lsp", "debug", "fetch", "web_search",
        };

        private readonly Func<string, Task<Snapshot?>> _read;
        private readonly IOmpLogger _logger;
        private readonly long _maxBaselineChars;
        private readonly SemaphoreSlim _applying = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();

        /// <summary>Baseline content per absolute path; null = file did not exist.</summary>
        private readonly PathMap<string?> _baselines = new PathMap<string?>();
        /// <summary>Tool call whose start snapshot set each baseline; that call's own reported <c>oldText</c> replaces it.</summary>
        private readonly PathMap<string> _baselineSource = new PathMap<string>();
        private readonly PathMap<TrackedChange> _byPath = new PathMap<TrackedChange>();
        /// <summary>Tool calls that started and have not ended.</summary>
        private readonly HashSet<string> _running = new HashSet<string>(StringComparer.Ordinal);
        private long _retainedChars;

        public ChangeModel(Func<string, Task<Snapshot?>> read, IOmpLogger logger)
            : this(read, logger, MaxBaselineChars)
        {
        }

        internal ChangeModel(Func<string, Task<Snapshot?>> read, IOmpLogger logger, long maxBaselineChars)
        {
            _read = read;
            _logger = logger;
            _maxBaselineChars = maxBaselineChars;
        }

        /// <summary>Reads a file for change tracking: <see cref="Snapshot.Missing"/> when absent, null when it cannot be tracked.</summary>
        public static async Task<Snapshot?> ReadSnapshotAsync(string path, IOmpLogger logger)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    logger.Debug($"Not tracking {path}: not a regular file");
                    return null;
                }
                var info = new FileInfo(path);
                if (!info.Exists) return Snapshot.Missing;
                if (info.Length > MaxSnapshotBytes)
                {
                    logger.Debug($"Not tracking {path}: {info.Length} bytes exceeds the {MaxSnapshotBytes}-byte snapshot limit");
                    return null;
                }
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false))
                {
                    return Snapshot.Of(await reader.ReadToEndAsync().ConfigureAwait(false));
                }
            }
            catch (Exception error) when (error is FileNotFoundException || error is DirectoryNotFoundException)
            {
                return Snapshot.Missing;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            {
                logger.Warn($"Cannot snapshot {path}", error);
                return null;
            }
        }

        public IReadOnlyList<TrackedChange> Changes
        {
            get { lock (_sync) return _byPath.Values().ToArray(); }
        }

        public TrackedChange? Change(string path)
        {
            lock (_sync) return _byPath.TryGet(path, out var change) ? change : null;
        }

        public bool HasBaseline(string path)
        {
            lock (_sync) return _baselines.Has(path);
        }

        /// <summary>First-seen content; <see cref="Snapshot.Missing"/> when the file did not exist, null when unknown.</summary>
        public Snapshot? Baseline(string path)
        {
            lock (_sync)
            {
                if (!_baselines.TryGet(path, out var content)) return null;
                return content == null ? Snapshot.Missing : Snapshot.Of(content);
            }
        }

        /// <summary>Forgets every baseline and change.</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _baselines.Clear();
                _baselineSource.Clear();
                _byPath.Clear();
                _running.Clear();
                _retainedChars = 0;
            }
        }

        public async Task<ApplyResult> ApplyAsync(ToolExecutionEvent e, ChangeScope scope)
        {
            await _applying.WaitAsync().ConfigureAwait(false);
            try
            {
                return await ApplyCoreAsync(e, scope).ConfigureAwait(false);
            }
            finally
            {
                _applying.Release();
            }
        }

        private async Task<ApplyResult> ApplyCoreAsync(ToolExecutionEvent e, ChangeScope scope)
        {
            var changesChanged = false;
            ApplyResult Result() => new ApplyResult { ChangesChanged = changesChanged };
            if (ReadOnlyTools.Contains(e.Name)) return Result();

            string? Resolve(string raw)
            {
                var filePath = ChangePaths.ResolveToolPath(raw, scope.Cwd);
                if (filePath == null || ChangePaths.IsWithinRoots(filePath, scope.Roots)) return filePath;
                _logger.Debug($"Not tracking {filePath}: outside the workspace");
                return null;
            }

            var argPaths = ChangePaths.ToolArgPaths(e.Args).Select(Resolve).Where(p => p != null).Select(p => p!).ToList();
            if (e.Phase == ToolExecutionPhase.Start)
            {
                lock (_sync) _running.Add(e.ToolCallId);
                foreach (var filePath in argPaths)
                {
                    lock (_sync) if (_baselines.Has(filePath)) continue;
                    var snapshot = await _read(filePath).ConfigureAwait(false);
                    if (snapshot == null) continue;
                    lock (_sync)
                    {
                        var content = snapshot.Content;
                        if (!Admit(filePath, content)) continue;
                        SetBaseline(filePath, content);
                        _baselineSource.Set(filePath, e.ToolCallId);
                    }
                }
                return Result();
            }

            var reported = new PathMap<string?>();
            lock (_sync)
            {
                _running.Remove(e.ToolCallId);
                foreach (var file in ChangePaths.ToolResultFiles(e.Result?.Details))
                {
                    var resolved = Resolve(file.Path);
                    if (resolved != null && !reported.Has(resolved)) reported.Set(resolved, file.OldText);
                }
                foreach (var filePath in reported.Keys())
                {
                    reported.TryGet(filePath, out var oldText);
                    if (oldText == null || !_baselineSource.TryGet(filePath, out var source) || source != e.ToolCallId) continue;
                    SetBaseline(filePath, oldText);
                }
            }

            var candidates = new PathMap<bool>();
            lock (_sync)
            {
                foreach (var filePath in argPaths) if (_baselines.Has(filePath)) candidates.Set(filePath, true);
            }
            foreach (var filePath in reported.Keys()) candidates.Set(filePath, true);

            foreach (var filePath in candidates.Keys())
            {
                var current = await _read(filePath).ConfigureAwait(false);
                if (current == null) continue;
                lock (_sync)
                {
                    if (!_baselines.Has(filePath))
                    {
                        if (!reported.TryGet(filePath, out var oldText) || oldText == null)
                        {
                            _logger.Debug($"Not tracking {filePath}: its content before {e.Name} ran is unknown");
                            continue;
                        }
                        if (!Admit(filePath, oldText)) continue;
                        SetBaseline(filePath, oldText);
                    }
                    _baselines.TryGet(filePath, out var before);
                    var after = current.Content;
                    if (before == after)
                    {
                        changesChanged = _byPath.Delete(filePath) || changesChanged;
                        continue;
                    }
                    var status = before == null ? ChangeStatus.Added : after == null ? ChangeStatus.Deleted : ChangeStatus.Modified;
                    var (added, removed) = ChangePaths.LineDelta(before, after);
                    _byPath.Set(filePath, new TrackedChange { Path = _byPath.PathOf(filePath) ?? filePath, Status = status, Added = added, Removed = removed });
                    changesChanged = true;
                }
            }
            return Result();
        }

        private void SetBaseline(string filePath, string? content)
        {
            _baselines.TryGet(filePath, out var previous);
            _retainedChars += (content?.Length ?? 0) - (previous?.Length ?? 0);
            _baselines.Set(filePath, content);
        }

        /// <summary>
        /// Makes room for a new baseline by forgetting baselines of files that are unchanged and not being
        /// written by a running tool. False when the baselines of changed files alone leave no room.
        /// </summary>
        private bool Admit(string filePath, string? content)
        {
            var size = content?.Length ?? 0;
            foreach (var candidate in _baselines.Keys())
            {
                if (_retainedChars + size <= _maxBaselineChars) return true;
                if (_byPath.Has(candidate)) continue;
                if (_baselineSource.TryGet(candidate, out var source) && _running.Contains(source)) continue;
                _baselines.TryGet(candidate, out var dropped);
                _retainedChars -= dropped?.Length ?? 0;
                _baselines.Delete(candidate);
                _baselineSource.Delete(candidate);
            }
            if (_retainedChars + size <= _maxBaselineChars) return true;
            _logger.Warn($"Not tracking changes to {filePath}: baselines already hold {_retainedChars} characters (limit {_maxBaselineChars})");
            return false;
        }

        /// <summary>Map keyed by absolute Windows path, compared case-insensitively; keys keep the spelling first seen.</summary>
        private sealed class PathMap<TValue>
        {
            private readonly Dictionary<string, KeyValuePair<string, TValue>> _entries = new Dictionary<string, KeyValuePair<string, TValue>>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();

            private static string Key(string path) => (ChangePaths.Normalize(path) ?? path).ToLowerInvariant();

            public bool Has(string path) => _entries.ContainsKey(Key(path));

            public bool TryGet(string path, out TValue value)
            {
                if (_entries.TryGetValue(Key(path), out var entry))
                {
                    value = entry.Value;
                    return true;
                }
                value = default!;
                return false;
            }

            public string? PathOf(string path) => _entries.TryGetValue(Key(path), out var entry) ? entry.Key : null;

            public void Set(string path, TValue value)
            {
                var key = Key(path);
                if (_entries.TryGetValue(key, out var existing))
                {
                    _entries[key] = new KeyValuePair<string, TValue>(existing.Key, value);
                    return;
                }
                _entries[key] = new KeyValuePair<string, TValue>(path, value);
                _order.Add(key);
            }

            public bool Delete(string path)
            {
                var key = Key(path);
                if (!_entries.Remove(key)) return false;
                _order.Remove(key);
                return true;
            }

            public void Clear()
            {
                _entries.Clear();
                _order.Clear();
            }

            public IReadOnlyList<string> Keys() => _order.Select(key => _entries[key].Key).ToArray();

            public IReadOnlyList<TValue> Values() => _order.Select(key => _entries[key].Value).ToArray();
        }
    }
}
