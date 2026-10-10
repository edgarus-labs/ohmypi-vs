using Microsoft.Win32.SafeHandles;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Omp.Core.Processes;

/// <summary>
/// Ends a process and every descendant with <c>TerminateProcess</c>. Descendants are found by parent process id in
/// Toolhelp snapshots, so nothing depends on WMI: <c>taskkill /T</c> queries WMI and blocks for about a minute, then
/// fails, when the WMI service is slow or stuck. Every member's handle stays open until the walk ends, so a member's
/// pid cannot be reused meanwhile, and a process only counts as a child when it was created after its parent, so an
/// unrelated process naming an older holder of the same pid as its parent is left alone.
/// </summary>
internal static class ProcessTree
{
    /// <summary>The operating system calls the walk makes; tests substitute their own.</summary>
    internal interface INative
    {
        /// <summary>Opens a process for ending; <paramref name="error"/> is the Win32 error when the handle is invalid.</summary>
        SafeProcessHandle Open(int pid, out int error);

        bool GetCreated(SafeProcessHandle process, out long created);

        bool Terminate(SafeProcessHandle process);

        bool TryGetExitCode(SafeProcessHandle process, out uint code);

        List<(int Pid, int ParentPid)> Parents();
    }

    private sealed class Windows : INative
    {
        public static readonly Windows Instance = new Windows();

        /// <summary>
        /// Opens a process handle for the specified process identifier with terminate and limited information query access, outputting the Win32 error code if the operation fails.
        /// </summary>
        /// <param name="pid">The unique identifier of the p.</param>
        /// <param name="error">The error.</param>
        /// <returns>The safe process handle result.</returns>
        public SafeProcessHandle Open(int pid, out int error)
        {
            var handle = NativeMethods.OpenProcess(NativeMethods.ProcessTerminate | NativeMethods.ProcessQueryLimitedInformation, false, pid);
            error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;

            return handle;
        }

        /// <summary>
        /// Retrieves the creation time of the specified process via its safe handle.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="created">The created.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool GetCreated(SafeProcessHandle process, out long created) => NativeMethods.GetProcessTimes(process, out created, out _, out _, out _);

        /// <summary>
        /// Terminates the specified process using its safe handle.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <returns>true if the operation succeeded; otherwise, false.</returns>
        public bool Terminate(SafeProcessHandle process) => NativeMethods.TerminateProcess(process, 1);

        /// <summary>
        /// Attempts to retrieve the exit code of the specified process.
        /// </summary>
        /// <param name="process">The process.</param>
        /// <param name="code">The code.</param>
        /// <returns>true if the condition is met; otherwise, false.</returns>
        public bool TryGetExitCode(SafeProcessHandle process, out uint code) => NativeMethods.GetExitCodeProcess(process, out code);

        /// <summary>
        /// Retrieves a list of process identifiers and their corresponding parent process identifiers.
        /// </summary>
        /// <returns>A collection of list items.</returns>
        public List<(int Pid, int ParentPid)> Parents() => NativeMethods.ProcessParents();
    }

    /// <summary>Snapshots taken after the first, to catch children started while their parent was being ended.</summary>
    private const int MaxRounds = 8;

    /// <summary>Terminate <paramref name="pid"/> and its descendants; failures are logged. Returns at once when <paramref name="pid"/> is gone.</summary>
    public static void Kill(int pid, IOmpLogger logger, INative? native = null)
    {
        native = native ?? Windows.Instance;
        var members = new Dictionary<int, Member>();
        try
        {
            var root = Open(pid, logger, native);
            if (root is null)
            {
                return;
            }

            members.Add(pid, root);
            Terminate(pid, root.Handle, logger, native);
            for (var round = 0; round < MaxRounds; round++)
            {
                if (!AddDescendants(members, native.Parents(), logger, native))
                {
                    return;
                }
            }
            logger.Warn($"Process {pid} kept starting children while its tree was being ended");
        }
        catch (Win32Exception error)
        {
            logger.Warn($"Cannot list the processes of the tree of {pid}", error);
        }
        finally
        {
            foreach (var member in members.Values)
            {
                member.Handle.Dispose();
            }
        }
    }

    /// <summary>Adds and terminates every process of <paramref name="processes"/> descending from a member; false when there was none.</summary>
    private static bool AddDescendants(Dictionary<int, Member> members, List<(int Pid, int ParentPid)> processes, IOmpLogger logger, INative native)
    {
        var added = false;
        bool grew;
        do
        {
            grew = false;
            foreach (var (pid, parentPid) in processes)
            {
                if (pid == parentPid || members.ContainsKey(pid) || !members.TryGetValue(parentPid, out var parent))
                {
                    continue;
                }

                var child = Open(pid, logger, native);
                if (child is null)
                {
                    continue;
                }

                if (child.Created < parent.Created)
                {
                    child.Handle.Dispose();
                    continue;
                }
                members.Add(pid, child);
                Terminate(pid, child.Handle, logger, native);
                grew = added = true;
            }
        }
        while (grew);

        return added;
    }

    /// <summary>
    /// Attempts to open a process by its identifier and retrieve its creation time to initialize a new Member instance.
    /// </summary>
    /// <param name="pid">The unique identifier of the p.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="native">The native.</param>
    /// <returns>The member? result.</returns>
    private static Member? Open(int pid, IOmpLogger logger, INative native)
    {
        var handle = native.Open(pid, out var error);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            if (error != NativeMethods.ErrorInvalidParameter)
            {
                logger.Warn($"Cannot open process {pid} to end it", new Win32Exception(error));
            }

            return null;
        }
        if (!native.GetCreated(handle, out var created))
        {
            logger.Warn($"Cannot read the start time of process {pid}", new Win32Exception());
            handle.Dispose();

            return null;
        }

        return new Member(handle, created);
    }

    /// <summary>
    /// Attempts to terminate the specified process using the provided handle and logs a warning if the process cannot be ended and remains active.
    /// </summary>
    /// <param name="pid">The unique identifier of the p.</param>
    /// <param name="handle">The handle.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="native">The native.</param>
    private static void Terminate(int pid, SafeProcessHandle handle, IOmpLogger logger, INative native)
    {
        if (native.Terminate(handle))
        {
            return;
        }

        var error = new Win32Exception();
        if (native.TryGetExitCode(handle, out var code) && code != NativeMethods.StillActive)
        {
            return;
        }

        logger.Warn($"Cannot end process {pid}", error);
    }

    /// <summary>
    /// Represents a member entity containing a process handle and its associated creation timestamp.
    /// </summary>
    private sealed class Member
    {
        /// <summary>
        /// Initializes a new instance of the Member class with the specified process handle and creation timestamp.
        /// </summary>
        /// <param name="handle">The handle.</param>
        /// <param name="created">The created.</param>
        public Member(SafeProcessHandle handle, long created)
        {
            Handle = handle;
            Created = created;
        }

        /// <summary>
        /// Gets the handle.
        /// </summary>
        public SafeProcessHandle Handle { get; }

        /// <summary>
        /// Gets the created.
        /// </summary>
        public long Created { get; }
    }
}
