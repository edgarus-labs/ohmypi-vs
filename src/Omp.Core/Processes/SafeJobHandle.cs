using Microsoft.Win32.SafeHandles;

namespace Omp.Core.Processes;

/// <summary>
/// Represents a safe wrapper for a job handle to ensure the underlying resource is correctly released.
/// </summary>
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the SafeJobHandle class.
    /// </summary>
    public SafeJobHandle()
        : base(true)
    {
    }

    /// <summary>
    /// Releases the underlying native system handle by invoking the CloseHandle function.
    /// </summary>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
