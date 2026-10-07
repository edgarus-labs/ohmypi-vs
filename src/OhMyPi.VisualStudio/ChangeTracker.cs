using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio;

/// <summary>
/// Files changed by OMP tools with Visual Studio's native diff: the content before OMP changed a file is written to a
/// read-only temporary file (left) and compared with the live file (right). Clearing the changes closes those diff
/// windows and deletes their temporary files.
/// </summary>
internal sealed class ChangeTracker : IDisposable
{
    /// <summary>Parent of every Visual Studio process's diff copies.</summary>
    public static readonly string DiffDirectory = Path.Combine(Path.GetTempPath(), "oh-my-pi", "diff");

    private readonly WorkspaceScope _scope;
    private readonly IOmpLogger _logger;
    private readonly AsyncPackage _package;
    private readonly Func<string, Task<bool>> _confirmOutside;
    private readonly ChangeFeed _feed;
    private readonly DiffBaselines _baselines;
    /// <summary>Open diff windows by their left copy; UI thread only.</summary>
    private readonly Dictionary<string, IVsWindowFrame> _frames = new Dictionary<string, IVsWindowFrame>(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <param name="confirmOutside">Asks on the UI thread whether a path outside <paramref name="scope"/> may be opened.</param>
    public ChangeTracker(IOmpService service, WorkspaceScope scope, IOmpLogger logger, AsyncPackage package, Func<string, Task<bool>> confirmOutside)
    {
        _scope = scope;
        _logger = logger;
        _package = package;
        _confirmOutside = confirmOutside;
        _baselines = new DiffBaselines(DiffDirectory, Process.GetCurrentProcess().Id);
        _feed = new ChangeFeed(service, new ChangeModel(path => ChangeModel.ReadSnapshotAsync(path, logger), logger), scope, logger);
        _feed.Cleared += OnCleared;
    }

    /// <summary>Raised on a background thread after <see cref="Changes"/> changed.</summary>
    public event EventHandler? ChangesChanged
    {
        add => _feed.ChangesChanged += value;
        remove => _feed.ChangesChanged -= value;
    }

    public IReadOnlyList<TrackedChange> Changes => _feed.Changes;

    public void Clear() => _feed.Clear();

    /// <summary>
    /// Opens the diff of a path named by OMP: against its tracked first-seen content, else against
    /// <paramref name="recordedBefore"/>; paths outside the workspace need consent. Does nothing once this tracker's
    /// chat generation is gone.
    /// </summary>
    /// <exception cref="InvalidOperationException">The path names no local file, or no diff was recorded for it.</exception>
    public async Task OpenDiffAsync(string rawPath, string? recordedBefore)
    {
        if (_disposed)
        {
            return;
        }

        var path = _scope.Resolve(rawPath);
        var source = await _feed.DiffSourceAsync(path, recordedBefore).ConfigureAwait(false);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        if (!await _scope.MayOpenAsync(path, _confirmOutside))
        {
            return;
        }

        var batch = _baselines.Batch;

        await TaskScheduler.Default;
        var name = Path.GetFileName(path);
        var left = _baselines.Write(batch, path, source.Before);
        var right = source.Deleted ? _baselines.Write(batch, path + ".deleted", "") : path;

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        var diff = await _package.GetServiceAsync<SVsDifferenceService, IVsDifferenceService>();
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        if (_disposed)
        {
            return;
        }

        if (batch != _baselines.Batch)
        {
            throw new InvalidOperationException("The OMP changes were cleared while the diff was opening.");
        }

        if (_frames.TryGetValue(left, out var open) && ErrorHandler.Succeeded(open.Show()))
        {
            return;
        }

        var options = __VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary | __VSDIFFSERVICEOPTIONS.VSDIFFOPT_DetectBinaryFiles;
        if (source.Deleted)
        {
            options |= __VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary;
        }

        var frame = diff.OpenComparisonWindow2(
            left,
            right,
            $"{name} (OMP changes)",
            path,
            $"{name} (before OMP)",
            source.Deleted ? $"{name} (deleted)" : $"{name} (current)",
            null,
            null,
            (uint)options);
        if (frame is null)
        {
            return;
        }

        _frames[left] = frame;
        if (frame is IVsWindowFrame2 watchable)
        {
            watchable.Advise(new FrameClosed(() => _frames.Remove(left)), out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _feed.Cleared -= OnCleared;
        _feed.Dispose();
        Background.Run(_package.JoinableTaskFactory, _logger, "Closing OMP change diffs", () => CloseDiffsAsync(_baselines.Root));
    }

    private void OnCleared(object sender, EventArgs e) => Background.Run(_package.JoinableTaskFactory, _logger, "Closing OMP change diffs", async () =>
                                                               {
                                                                   await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                                                                   await CloseDiffsAsync(_baselines.Rotate());
                                                               });

    /// <summary>Closes every diff window, then deletes <paramref name="directory"/> with the copies they showed.</summary>
    private async Task CloseDiffsAsync(string directory)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
        foreach (var frame in _frames.Values.ToArray())
        {
            var result = frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
            if (ErrorHandler.Failed(result))
            {
                _logger.Warn($"Could not close an OMP diff window (HRESULT 0x{result:X8})");
            }
        }
        _frames.Clear();
        await TaskScheduler.Default;
        try
        {
            DiffBaselines.Delete(directory);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            _logger.Warn($"Could not delete the OMP diff copies in {directory}", error);
        }
    }

    private sealed class FrameClosed : IVsWindowFrameNotify
    {
        private readonly Action _closed;

        public FrameClosed(Action closed)
        {
            _closed = closed;
        }

        public int OnShow(int fShow)
        {
            if (fShow == (int)__FRAMESHOW.FRAMESHOW_WinClosed)
            {
                _closed();
            }

            return VSConstants.S_OK;
        }

        public int OnMove() => VSConstants.S_OK;

        public int OnSize() => VSConstants.S_OK;

        public int OnDockableChange(int fDockable) => VSConstants.S_OK;
    }
}
