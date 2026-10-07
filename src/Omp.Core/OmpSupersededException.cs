using System;

namespace Omp.Core;

/// <summary>A start or restart was cancelled by a later restart or stop; it is not a failure to report.</summary>
public sealed class OmpSupersededException : OperationCanceledException
{
    public OmpSupersededException(Exception? inner = null)
        : base("OMP start was superseded by a restart or stop", inner)
    {
    }
}
