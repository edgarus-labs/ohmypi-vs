using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class OmpServiceOptions
{
    /// <summary>Resolved absolute executable path.</summary>
    public string Executable { get; set; } = "";

    /// <summary>Arguments appended after "--mode rpc-ui".</summary>
    public IReadOnlyList<string> ExtraArgs { get; set; } = Array.Empty<string>();

    public string Cwd { get; set; } = "";

    /// <summary>Environment overrides for the OMP process (null value removes a variable).</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; set; }

    public IOmpLogger Logger { get; set; } = null!;

    public bool AutoRestart { get; set; } = true;

    /// <summary>Tools this client provides to the agent; registered with every OMP process the service starts.</summary>
    public IHostTools? HostTools { get; set; }
}
