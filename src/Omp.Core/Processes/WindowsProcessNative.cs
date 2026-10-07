using System;
using System.Runtime.InteropServices;

namespace Omp.Core.Processes;

internal sealed class WindowsProcessNative : IProcessNative
{
    public static readonly WindowsProcessNative Instance = new WindowsProcessNative();

    public SafeJobHandle CreateJob() => NativeMethods.CreateJobObject(IntPtr.Zero, null);

    public bool SetKillOnClose(SafeJobHandle job)
    {
        var limits = new NativeMethods.JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;

        return NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformationClass, ref limits, Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>());
    }

    public bool AssignToJob(SafeJobHandle job, IntPtr process) => NativeMethods.AssignProcessToJobObject(job, process);

    public int Resume(IntPtr thread) => NativeMethods.ResumeThread(thread);

    public bool TerminateJob(SafeJobHandle job) => NativeMethods.TerminateJobObject(job, 1);
}
