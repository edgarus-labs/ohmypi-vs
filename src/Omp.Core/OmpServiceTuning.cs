using Omp.Core.Processes;
using Omp.Core.Protocol;
using System;
using System.Diagnostics;

namespace Omp.Core;

/// <summary>Restart/shutdown timing and process creation; production uses the defaults.</summary>
internal sealed class OmpServiceTuning
{
    /// <summary>Delay before each automatic restart; its length is the restart budget per window.</summary>
    public int[] RestartDelaysMs { get; set; } = [500, 2000, 5000];

    /// <summary>
    /// Gets or sets the restart window ms.
    /// </summary>
    public int RestartWindowMs { get; set; } = 60_000;

    /// <summary>
    /// Gets or sets the shutdown grace ms.
    /// </summary>
    public int ShutdownGraceMs { get; set; } = 3000;

    /// <summary>
    /// Gets or sets the ready timeout ms.
    /// </summary>
    public int ReadyTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Gets or sets the history page limit.
    /// </summary>
    public int HistoryPageLimit { get; set; } = 256;

    /// <summary>Creates the OMP process; tests substitute an in-memory one.</summary>
    public Func<OmpProcessOptions, IOmpProcessHandle> Spawn { get; set; } = options => new OmpProcess(options);

    /// <summary>Milliseconds on a monotonic clock, used to time a turn; tests substitute a manual one.</summary>
    public Func<long> TurnClockMs { get; set; } = () => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
}
