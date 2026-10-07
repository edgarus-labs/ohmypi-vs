using System;

namespace Omp.Core;

/// <summary>Diagnostics sink. Implementations must never receive secrets; callers redact first.</summary>
public interface IOmpLogger
{
    void Error(string message, Exception? error = null);

    void Warn(string message, Exception? error = null);

    void Info(string message, Exception? error = null);

    void Debug(string message, Exception? error = null);

    /// <summary>Whether RPC frames are traced right now; read per frame.</summary>
    bool TraceEnabled { get; }

    /// <summary>One already-redacted RPC frame. <paramref name="direction"/> is "in" or "out".</summary>
    void Trace(string direction, string frame);
}
