using Microsoft.Win32.SafeHandles;
using Omp.Core.Internal;
using Omp.Core.Protocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Omp.Core.Processes;

/// <summary>
/// One <c>omp --mode rpc-ui</c> child process started suspended inside its own kill-on-close Windows job object, so
/// the whole tree normally dies with this handle, even if Visual Studio crashes. When the job cannot be created or
/// assigned, the tree is ended by walking it (<see cref="ProcessTree"/>) on shutdown only, and a crash of Visual Studio
/// leaves it running. Stdin/stdout/stderr are raw UTF-8 byte pipes and no other handle is inherited. The child never searches
/// its working directory for executables (<c>NoDefaultCurrentDirectoryInExePath</c>), so a <c>node.exe</c> planted in
/// an opened folder is not run by the npm <c>omp.cmd</c> launcher.
/// </summary>
internal sealed class OmpProcess : IOmpProcessHandle
{
    /// <summary>How long to wait for stdio to drain after the process exits before reporting the close.</summary>
    private const int StdioDrainMs = 1000;
    private const int StderrTailLines = 20;
    private const int StderrTailChars = 4000;
    private const int ReadBufferBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    private readonly OmpProcessOptions _options;
    private readonly IOmpLogger _logger;
    private readonly object _sync = new object();
    private readonly BlockingCollection<PendingWrite> _writes = new BlockingCollection<PendingWrite>();
    private readonly TaskCompletionSource<bool> _exitedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<TransportClose> _closed = new TaskCompletionSource<TransportClose>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stdoutDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<string> _stderrLines = new Queue<string>();
    private readonly StringBuilder _stderrPartial = new StringBuilder();
    private SafeJobHandle? _job;
    private SafeWaitHandle? _processHandle;
    private RegisteredWaitHandle? _exitWait;
    private FileStream? _stdin;
    private bool _started;
    private volatile bool _exited;
    private bool _reported;
    private Task? _shutdown;

    public OmpProcess(OmpProcessOptions options)
    {
        _options = options;
        _logger = options.Logger;
        Native = options.Native ?? WindowsProcessNative.Instance;
    }

    private IProcessNative Native { get; }

    /// <remarks>The segment is only valid during the callback; a subscriber that keeps the bytes must copy them.</remarks>
    public event Action<ArraySegment<byte>>? Data;

    public event Action<TransportClose>? Closed;

    public int? Pid { get; private set; }

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                throw new InvalidOperationException("OMP process already started");
            }

            _started = true;
            try
            {
                if (_shutdown is not null)
                {
                    throw new IOException("OMP process was shut down before it started");
                }

                Launch();
            }
            catch (Exception error) when (error is Win32Exception || error is IOException)
            {
                _exited = true;
                _exitedSignal.TrySetResult(true);
                _writes.CompleteAdding();
                ThreadPool.QueueUserWorkItem(_ => Report(new TransportClose { Error = error }));
            }
        }
    }

    public void Write(string line, Action<Exception>? onError = null)
    {
        lock (_sync)
        {
            if (!_started || _exited || _shutdown is not null || _writes.IsAddingCompleted)
            {
                throw new InvalidOperationException("OMP process is not running");
            }

            _writes.Add(new PendingWrite(Utf8.GetBytes(line), onError));
        }
    }

    /// <summary>
    /// Close stdin and keep reading stdout until OMP exits; after <paramref name="graceMs"/> terminate the job (the
    /// whole process tree), or walk the tree when there is no usable job; then walk the tree as well; then fail. Idempotent.
    /// </summary>
    public Task ShutdownAsync(int graceMs)
    {
        lock (_sync)
        {
            _shutdown ??= RunShutdownAsync(graceMs);

            return _shutdown;
        }
    }

    private void Launch()
    {
        var args = new List<string> { "--mode", "rpc-ui" };
        args.AddRange(_options.Args);
        var (application, commandLine) = CommandLine.Build(_options.Executable, args, System.Environment.GetEnvironmentVariable("ComSpec"));
        _logger.Debug($"Spawning {commandLine} (cwd {_options.Cwd})");

        SafeFileHandle? stdinRead = null, stdinWrite = null, stdoutRead = null, stdoutWrite = null, stderrRead = null, stderrWrite = null;
        var attributeList = IntPtr.Zero;
        var handleList = IntPtr.Zero;
        var environment = IntPtr.Zero;
        var attributeListInitialized = false;
        try
        {
            CreatePipe(out stdinRead, out stdinWrite);
            CreatePipe(out stdoutRead, out stdoutWrite);
            CreatePipe(out stderrRead, out stderrWrite);

            var size = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception();
            }

            attributeListInitialized = true;
            handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handleList, 0, stdinRead.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, IntPtr.Size, stdoutWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, stderrWrite.DangerousGetHandle());
            if (!NativeMethods.UpdateProcThreadAttribute(attributeList, 0, NativeMethods.ProcThreadAttributeHandleList, handleList, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception();
            }

            var startup = new NativeMethods.StartupInfoEx();
            startup.StartupInfo.Cb = Marshal.SizeOf<NativeMethods.StartupInfoEx>();
            startup.StartupInfo.Flags = NativeMethods.StartfUseStdHandles;
            startup.StartupInfo.StdInput = stdinRead.DangerousGetHandle();
            startup.StartupInfo.StdOutput = stdoutWrite.DangerousGetHandle();
            startup.StartupInfo.StdError = stderrWrite.DangerousGetHandle();
            startup.AttributeList = attributeList;
            environment = Marshal.StringToHGlobalUni(CommandLine.EnvironmentBlock(ChildEnvironment(_options.Environment)));
            MakeInheritable(stdinRead);
            MakeInheritable(stdoutWrite);
            MakeInheritable(stderrWrite);

            const int flags = NativeMethods.CreateSuspended | NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateNoWindow | NativeMethods.ExtendedStartupInfoPresent;
            if (!NativeMethods.CreateProcess(application, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, true, flags, environment, _options.Cwd, ref startup, out var info))
            {
                var error = new Win32Exception();

                throw new IOException($"Cannot start {_options.Executable}: {error.Message} (Win32 error {error.NativeErrorCode})", error);
            }
            try
            {
                _job = _options.UseJobObject ? CreateJob(info.Process) : null;
                if (Native.Resume(info.Thread) == -1)
                {
                    throw new Win32Exception();
                }
            }
            catch
            {
                NativeMethods.TerminateProcess(info.Process, 1);
                NativeMethods.CloseHandle(info.Process);
                _job?.Dispose();
                _job = null;

                throw;
            }
            finally
            {
                NativeMethods.CloseHandle(info.Thread);
            }
            _processHandle = new SafeWaitHandle(info.Process, true);
            Pid = info.ProcessId;
        }
        finally
        {
            if (attributeListInitialized)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }

            if (attributeList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(attributeList);
            }

            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
            }

            if (environment != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environment);
            }

            stdinRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrWrite?.Dispose();
            if (Pid is null)
            {
                stdinWrite?.Dispose();
                stdoutRead?.Dispose();
                stderrRead?.Dispose();
            }
        }

        _stdin = new FileStream(stdinWrite!, FileAccess.Write, 1, false);
        StartThread("OMP stdout", () => ReadStdout(new FileStream(stdoutRead!, FileAccess.Read, 1, false)));
        StartThread("OMP stderr", () => ReadStderr(new FileStream(stderrRead!, FileAccess.Read, 1, false)));
        StartThread("OMP stdin", WriteStdin);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(new ProcessWaitHandle(_processHandle), (_, __) => OnExit(), null, Timeout.Infinite, true);
    }

    /// <summary>
    /// An anonymous pipe whose ends are not inheritable, so a process another thread of the host starts with inherited
    /// handles cannot capture them; the child's end is made inheritable just before <c>CreateProcess</c>.
    /// </summary>
    internal static void CreatePipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        var attributes = new NativeMethods.SecurityAttributes { Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(), InheritHandle = 0 };
        if (!NativeMethods.CreatePipe(out read, out write, ref attributes, 0))
        {
            throw new Win32Exception();
        }
    }

    /// <summary>
    /// The caller's overrides plus <c>NoDefaultCurrentDirectoryInExePath=1</c>: cmd.exe running a <c>.cmd</c> launcher
    /// would otherwise find a bare <c>node</c> or <c>bun</c> in the working directory before PATH.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> ChildEnvironment(IReadOnlyDictionary<string, string?>? overrides)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        environment["NoDefaultCurrentDirectoryInExePath"] = "1";

        return environment;
    }

    private static void MakeInheritable(SafeFileHandle handle)
    {
        if (!NativeMethods.SetHandleInformation(handle, NativeMethods.HandleFlagInherit, NativeMethods.HandleFlagInherit))
        {
            throw new Win32Exception();
        }
    }

    private SafeJobHandle? CreateJob(IntPtr process)
    {
        var job = Native.CreateJob();
        if (job.IsInvalid)
        {
            _logger.Warn("Cannot create a job object for OMP; its process tree is ended on shutdown only", new Win32Exception());
            job.Dispose();

            return null;
        }
        if (!Native.SetKillOnClose(job) || !Native.AssignToJob(job, process))
        {
            _logger.Warn("Cannot place OMP in a kill-on-close job object; its process tree is ended on shutdown only", new Win32Exception());
            job.Dispose();

            return null;
        }

        return job;
    }

    private static void StartThread(string name, ThreadStart body) =>
        new Thread(body) { IsBackground = true, Name = name }.Start();

    private void ReadStdout(FileStream stream)
    {
        var buffer = new byte[ReadBufferBytes];
        try
        {
            using (stream)
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    Listeners.Raise(Data, nameof(Data), new ArraySegment<byte>(buffer, 0, read), _logger);
                }
            }
        }
        catch (IOException error)
        {
            if (!_exited)
            {
                _logger.Warn("OMP stdout error", error);
            }
        }
        finally
        {
            _stdoutDone.TrySetResult(true);
        }
    }

    private void ReadStderr(FileStream stream)
    {
        var buffer = new byte[ReadBufferBytes];
        var decoder = Utf8.GetDecoder();
        var chars = new char[Utf8.GetMaxCharCount(buffer.Length)];
        try
        {
            using (stream)
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var count = decoder.GetChars(buffer, 0, read, chars, 0);
                    Stderr(chars, count);
                }
            }
        }
        catch (IOException error)
        {
            _logger.Debug("OMP stderr error", error);
        }
        finally
        {
            _stderrDone.TrySetResult(true);
        }
    }

    private void Stderr(char[] chars, int count)
    {
        lock (_stderrLines)
        {
            for (var i = 0; i < count; i++)
            {
                var c = chars[i];
                if (c == '\n')
                {
                    var line = _stderrPartial.ToString();
                    _stderrPartial.Clear();
                    StderrLine(line.EndsWith("\r", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line);
                }
                else if (_stderrPartial.Length < StderrTailChars)
                {
                    _stderrPartial.Append(c);
                }
                else if (_stderrPartial.Length == StderrTailChars)
                {
                    _stderrPartial.Append('…');
                }
            }
        }
    }

    private void StderrLine(string line)
    {
        if (line.Trim().Length == 0)
        {
            return;
        }

        _logger.Debug($"omp: {line}");
        _stderrLines.Enqueue(line);
        if (_stderrLines.Count > StderrTailLines)
        {
            _stderrLines.Dequeue();
        }
    }

    private void WriteStdin()
    {
        var stdin = _stdin!;
        Exception? failure = null;
        foreach (var write in _writes.GetConsumingEnumerable())
        {
            if (failure is null)
            {
                try
                {
                    stdin.Write(write.Bytes, 0, write.Bytes.Length);
                    stdin.Flush();
                    continue;
                }
                catch (Exception error) when (error is IOException || error is ObjectDisposedException)
                {
                    failure = error;
                }
            }
            if (write.OnError is not null)
            {
                Listeners.Raise(write.OnError, "stdin write failure", failure, _logger);
            }
            else
            {
                _logger.Warn("Failed to write to OMP stdin", failure);
            }
        }
        try
        {
            stdin.Dispose();
        }
        catch (IOException error)
        {
            _logger.Debug("Closing OMP stdin failed", error);
        }
    }

    private void OnExit()
    {
        uint code = 0;
        var known = _processHandle is not null && NativeMethods.GetExitCodeProcess(_processHandle, out code);
        _exited = true;
        _exitedSignal.TrySetResult(true);
        CompleteWrites();
        KillLeftovers();
        _ = Task.WhenAny(Task.WhenAll(_stdoutDone.Task, _stderrDone.Task), Task.Delay(StdioDrainMs))
            .ContinueWith(_ => Report(new TransportClose { Code = known ? (int)code : (int?)null }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>No further writes are accepted; the writer flushes what is queued, then closes stdin.</summary>
    private void CompleteWrites()
    {
        lock (_sync)
        {
            _writes.CompleteAdding();
        }
    }

    /// <summary>Descendants that outlived OMP would be orphaned; the job ends them with their leader. False without a usable job.</summary>
    private bool KillLeftovers()
    {
        var job = Volatile.Read(ref _job);
        if (job is null || job.IsClosed)
        {
            return false;
        }

        try
        {
            if (Native.TerminateJob(job))
            {
                return true;
            }
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        _logger.Debug("Terminating OMP's job object failed", new Win32Exception());

        return false;
    }

    private async Task RunShutdownAsync(int graceMs)
    {
        bool started;
        lock (_sync)
        {
            started = _started;
        }

        if (!started)
        {
            return;
        }

        if (!_exited)
        {
            CompleteWrites();
            if (!await WaitForExitAsync(graceMs).ConfigureAwait(false))
            {
                _logger.Info($"OMP did not exit within {graceMs} ms of closing stdin; killing its process tree");
                KillTree(alwaysWalk: false);
                if (!await WaitForExitAsync(graceMs).ConfigureAwait(false))
                {
                    _logger.Warn("OMP survived killing its process tree; ending every process of the tree");
                    KillTree(alwaysWalk: true);
                    if (!await WaitForExitAsync(graceMs).ConfigureAwait(false))
                    {
                        throw new IOException($"OMP process {Pid} did not exit after its process tree was killed");
                    }
                }
            }
        }
        await _closed.Task.ConfigureAwait(false);
    }

    private async Task<bool> WaitForExitAsync(int ms)
    {
        if (_exited)
        {
            return true;
        }

        return await Task.WhenAny(_exitedSignal.Task, Task.Delay(ms)).ConfigureAwait(false) == _exitedSignal.Task;
    }

    /// <summary>Terminates the job; the tree is walked and each process ended when there is no usable job or on the last attempt.</summary>
    private void KillTree(bool alwaysWalk)
    {
        if ((KillLeftovers() && !alwaysWalk) || !(Pid is int pid))
        {
            return;
        }

        ProcessTree.Kill(pid, _logger, _options.TreeNative);
    }

    private void Report(TransportClose close)
    {
        string tail;
        lock (_stderrLines)
        {
            if (_reported)
            {
                return;
            }

            _reported = true;
            StderrLine(_stderrPartial.ToString());
            _stderrPartial.Clear();
            tail = string.Join("\n", _stderrLines);
        }
        _exitWait?.Unregister(null);
        Interlocked.Exchange(ref _job, null)?.Dispose();
        _processHandle?.Dispose();
        close.Pid = Pid;
        if (tail.Length > 0)
        {
            close.Stderr = tail.Length > StderrTailChars ? "…" + tail.Substring(tail.Length - StderrTailChars + 1) : tail;
        }

        Listeners.Raise(Closed, nameof(Closed), close, _logger);
        _closed.TrySetResult(close);
    }

    private sealed class PendingWrite
    {
        public PendingWrite(byte[] bytes, Action<Exception>? onError)
        {
            Bytes = bytes;
            OnError = onError;
        }

        public byte[] Bytes { get; }

        public Action<Exception>? OnError { get; }
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: false);
        }
    }
}
