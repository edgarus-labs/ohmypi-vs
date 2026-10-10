using System;
using System.Runtime.InteropServices;

namespace Omp.Core.Processes;

/// <summary>
/// Provides a Windows-specific implementation of the IProcessNative interface for managing native process lifecycles, job objects, and execution control.
/// </summary>
internal sealed class WindowsProcessNative : IProcessNative
{
    /// <summary>
    /// The instance.
    /// </summary>
    public static readonly WindowsProcessNative Instance = new WindowsProcessNative();

    /// <summary>
    /// Creates a new job object using the underlying native system API and returns a handle to manage its lifecycle.
    /// </summary>
    /// <returns>The safe job handle result.</returns>
    public SafeJobHandle CreateJob() => NativeMethods.CreateJobObject(IntPtr.Zero, null);

    /// <summary>
    /// Configures the specified job object to automatically terminate all associated processes when the job handle is closed.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool SetKillOnClose(SafeJobHandle job)
    {
        var limits = new NativeMethods.JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JobObjectLimitKillOnJobClose;

        return NativeMethods.SetInformationJobObject(job, NativeMethods.JobObjectExtendedLimitInformationClass, ref limits, Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>());
    }

    /// <summary>
    /// Assigns the specified process to the designated job object.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="process">The process.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool AssignToJob(SafeJobHandle job, IntPtr process) => NativeMethods.AssignProcessToJobObject(job, process);

    /// <summary>
    /// Resumes the execution of the specified thread.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The int result.</returns>
    public int Resume(IntPtr thread) => NativeMethods.ResumeThread(thread);

    /// <summary>
    /// Terminates all processes associated with the specified job object.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool TerminateJob(SafeJobHandle job) => NativeMethods.TerminateJobObject(job, 1);
}
