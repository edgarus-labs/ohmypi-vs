using System.Threading.Tasks;

namespace Omp.Core.Protocol;

/// <summary>OMP child process as the service drives it.</summary>
internal interface IOmpProcessHandle : IOmpTransport
{
    int? Pid { get; }

    /// <summary>Launch the process; called once after every listener is attached. A launch failure is reported as a close.</summary>
    void Start();

    /// <summary>Close stdin, wait, then terminate the whole process tree. Idempotent.</summary>
    Task ShutdownAsync(int graceMs);
}
