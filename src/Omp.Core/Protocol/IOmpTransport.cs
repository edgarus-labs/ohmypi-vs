using System;

namespace Omp.Core.Protocol;

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
