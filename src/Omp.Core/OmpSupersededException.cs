using System;

namespace Omp.Core;

/// <summary>A start or restart was cancelled by a later restart or stop; it is not a failure to report.</summary>
public sealed class OmpSupersededException : OperationCanceledException
{
    /// <summary>
    /// Initializes a new instance of the OmpSupersededException class with an optional inner exception.
    /// </summary>
    /// <param name="inner">The inner.</param>
    public OmpSupersededException(Exception? inner = null)
        : base("OMP start was superseded by a restart or stop", inner)
    {
    }
}
