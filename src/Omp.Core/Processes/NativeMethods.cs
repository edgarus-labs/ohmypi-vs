using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Omp.Core.Processes;

internal static class NativeMethods
{
    public const int HandleFlagInherit = 0x1;
    public const int StartfUseStdHandles = 0x100;
    public const int CreateSuspended = 0x4;
    public const int CreateUnicodeEnvironment = 0x400;
    public const int CreateNoWindow = 0x08000000;
    public const int ExtendedStartupInfoPresent = 0x00080000;
    public static readonly IntPtr ProcThreadAttributeHandleList = (IntPtr)0x20002;
    public const int JobObjectExtendedLimitInformationClass = 9;
    public const uint JobObjectLimitKillOnJobClose = 0x2000;
    public const int ProcessTerminate = 0x0001;
    public const int ProcessQueryLimitedInformation = 0x1000;
    public const uint StillActive = 259;
    public const int ErrorNoMoreFiles = 18;
    public const int ErrorInvalidParameter = 87;
    private const int Th32csSnapProcess = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfo
    {
        public int Cb;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Count;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    /// <summary>
    /// Represents extended startup information for a process, including standard startup settings and an associated attribute list.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        /// <summary>
        /// The startup info.
        /// </summary>
        public StartupInfo StartupInfo;
        /// <summary>
        /// The attribute list.
        /// </summary>
        public IntPtr AttributeList;
    }

    /// <summary>
    /// Represents the data structure containing process and thread identification details.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        /// <summary>
        /// The process.
        /// </summary>
        public IntPtr Process;
        /// <summary>
        /// The thread.
        /// </summary>
        public IntPtr Thread;
        /// <summary>
        /// The process id.
        /// </summary>
        public int ProcessId;
        /// <summary>
        /// The thread id.
        /// </summary>
        public int ThreadId;
    }

    /// <summary>
    /// Represents the basic resource limit information for a job object, including CPU time limits, memory working set sizes, and process scheduling constraints.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectBasicLimitInformation
    {
        /// <summary>
        /// The per process user time limit.
        /// </summary>
        public long PerProcessUserTimeLimit;
        /// <summary>
        /// The per job user time limit.
        /// </summary>
        public long PerJobUserTimeLimit;
        /// <summary>
        /// The limit flags.
        /// </summary>
        public uint LimitFlags;
        /// <summary>
        /// The minimum working set size.
        /// </summary>
        public UIntPtr MinimumWorkingSetSize;
        /// <summary>
        /// The maximum working set size.
        /// </summary>
        public UIntPtr MaximumWorkingSetSize;
        /// <summary>
        /// The active process limit.
        /// </summary>
        public uint ActiveProcessLimit;
        /// <summary>
        /// The affinity.
        /// </summary>
        public UIntPtr Affinity;
        /// <summary>
        /// The priority class.
        /// </summary>
        public uint PriorityClass;
        /// <summary>
        /// The scheduling class.
        /// </summary>
        public uint SchedulingClass;
    }

    /// <summary>
    /// Represents the input/output operation and transfer counts for a specific process or system resource.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        /// <summary>
        /// The read operation count.
        /// </summary>
        public ulong ReadOperationCount;
        /// <summary>
        /// The write operation count.
        /// </summary>
        public ulong WriteOperationCount;
        /// <summary>
        /// The other operation count.
        /// </summary>
        public ulong OtherOperationCount;
        /// <summary>
        /// The read transfer count.
        /// </summary>
        public ulong ReadTransferCount;
        /// <summary>
        /// The write transfer count.
        /// </summary>
        public ulong WriteTransferCount;
        /// <summary>
        /// The other transfer count.
        /// </summary>
        public ulong OtherTransferCount;
    }

    /// <summary>
    /// Represents the extended resource limit and memory usage information for a Windows job object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectExtendedLimitInformation
    {
        /// <summary>
        /// The basic limit information.
        /// </summary>
        public JobObjectBasicLimitInformation BasicLimitInformation;
        /// <summary>
        /// The io info.
        /// </summary>
        public IoCounters IoInfo;
        /// <summary>
        /// The process memory limit.
        /// </summary>
        public UIntPtr ProcessMemoryLimit;
        /// <summary>
        /// The job memory limit.
        /// </summary>
        public UIntPtr JobMemoryLimit;
        /// <summary>
        /// The peak process memory used.
        /// </summary>
        public UIntPtr PeakProcessMemoryUsed;
        /// <summary>
        /// The peak job memory used.
        /// </summary>
        public UIntPtr PeakJobMemoryUsed;
    }

    /// <summary>
    /// Represents a snapshot of a process&apos;s information, including its identifier, executable file name, and resource usage.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ProcessEntry32
    {
        /// <summary>
        /// The size.
        /// </summary>
        public int Size;
        /// <summary>
        /// The usage.
        /// </summary>
        public int Usage;
        /// <summary>
        /// The process id.
        /// </summary>
        public int ProcessId;
        /// <summary>
        /// The default heap id.
        /// </summary>
        public IntPtr DefaultHeapId;
        /// <summary>
        /// The module id.
        /// </summary>
        public int ModuleId;
        /// <summary>
        /// The threads.
        /// </summary>
        public int Threads;
        /// <summary>
        /// The parent process id.
        /// </summary>
        public int ParentProcessId;
        /// <summary>
        /// The priority class base.
        /// </summary>
        public int PriorityClassBase;
        /// <summary>
        /// The flags.
        /// </summary>
        public int Flags;

        /// <summary>
        /// The exe file.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SecurityAttributes attributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetHandleInformation(SafeHandle handle, int mask, int flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcess(
        string applicationName,
        System.Text.StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        int creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateJobObjectW")]
    public static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(SafeWaitHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(int flags, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    /// <summary>Every running process as (pid, parent pid), read from a Toolhelp snapshot; WMI is not involved.</summary>
    public static List<(int Pid, int ParentPid)> ProcessParents()
    {
        using (var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0))
        {
            if (snapshot.IsInvalid)
            {
                throw new Win32Exception();
            }

            var processes = new List<(int, int)>();
            var entry = new ProcessEntry32 { Size = Marshal.SizeOf<ProcessEntry32>() };
            var more = Process32First(snapshot, ref entry);
            while (more)
            {
                processes.Add((entry.ProcessId, entry.ParentProcessId));
                more = Process32Next(snapshot, ref entry);
            }
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNoMoreFiles)
            {
                throw new Win32Exception(error);
            }

            return processes;
        }
    }
}
