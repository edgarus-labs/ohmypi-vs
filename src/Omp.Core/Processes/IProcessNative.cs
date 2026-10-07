using System;

namespace Omp.Core.Processes;

/// <summary>The job object and thread calls that start and end OMP's process tree.</summary>
internal interface IProcessNative
{
    SafeJobHandle CreateJob();

    bool SetKillOnClose(SafeJobHandle job);

    bool AssignToJob(SafeJobHandle job, IntPtr process);

    /// <summary>The thread's previous suspend count, or -1 when it cannot be resumed.</summary>
    int Resume(IntPtr thread);

    bool TerminateJob(SafeJobHandle job);
}
