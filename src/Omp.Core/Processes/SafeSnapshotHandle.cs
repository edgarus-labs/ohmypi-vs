using Microsoft.Win32.SafeHandles;

namespace Omp.Core.Processes;

/// <summary>
/// Provides a safe wrapper for a system snapshot handle to ensure reliable resource cleanup and prevent handle leaks.
/// </summary>
internal sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the SafeSnapshotHandle class.
    /// </summary>
    public SafeSnapshotHandle()
        : base(true)
    {
    }

    /// <summary>
    /// Releases the underlying native system handle by calling the CloseHandle function.
    /// </summary>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
