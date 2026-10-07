using System;

namespace Omp.Core.Protocol;

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
