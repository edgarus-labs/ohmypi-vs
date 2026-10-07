using Newtonsoft.Json.Linq;
using System;

namespace Omp.Core.Protocol;

internal readonly struct ReassemblyResult
{
    public ReassemblyResult(JObject? frame, Exception? error)
    {
        Frame = frame;
        Error = error;
    }

    /// <summary>A complete logical frame ready for dispatch.</summary>
    public JObject? Frame { get; }

    /// <summary>The chunk (or the sequence it belonged to) was rejected.</summary>
    public Exception? Error { get; }
}
