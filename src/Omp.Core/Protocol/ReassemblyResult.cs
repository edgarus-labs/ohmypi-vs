using Newtonsoft.Json.Linq;
using System;

namespace Omp.Core.Protocol;

/// <summary>
/// Represents the outcome of a frame reassembly operation, containing either the resulting JSON object or the exception encountered during the process.
/// </summary>
internal readonly struct ReassemblyResult
{
    /// <summary>
    /// Initializes a new instance of the ReassemblyResult struct with the specified frame data and error information.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="error">The error.</param>
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
