using System;
using System.Threading.Tasks;

namespace Omp.Core.Protocol
{
    /// <summary>How an OMP transport ended.</summary>
    internal sealed class TransportClose
    {
        public int? Code { get; set; }

        /// <summary>Spawn or pipe failure that ended the transport.</summary>
        public Exception? Error { get; set; }

        /// <summary>Id of the process that ended, when it was spawned.</summary>
        public int? Pid { get; set; }

        /// <summary>The last lines OMP wrote to stderr, newline-separated and bounded; null when there were none.</summary>
        public string? Stderr { get; set; }
    }

    /// <summary>Byte stream to and from one OMP process (a child process, or in-memory in tests).</summary>
    internal interface IOmpTransport
    {
        /// <summary>
        /// Write one JSONL line. Throws when the transport can no longer accept input; <paramref name="onError"/>
        /// receives a failure the transport detects only after accepting the line.
        /// </summary>
        void Write(string line, Action<Exception>? onError = null);

        /// <remarks>The segment is only valid during the callback; a subscriber that keeps the bytes must copy them.</remarks>
        event Action<ArraySegment<byte>>? Data;

        event Action<TransportClose>? Closed;
    }

    /// <summary>OMP child process as the service drives it.</summary>
    internal interface IOmpProcessHandle : IOmpTransport
    {
        int? Pid { get; }

        /// <summary>Launch the process; called once after every listener is attached. A launch failure is reported as a close.</summary>
        void Start();

        /// <summary>Close stdin, wait, then terminate the whole process tree. Idempotent.</summary>
        Task ShutdownAsync(int graceMs);
    }
}
