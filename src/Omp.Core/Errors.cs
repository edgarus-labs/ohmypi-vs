using System;

namespace Omp.Core
{
    /// <summary>
    /// An OMP command failed. <see cref="Code"/> is OMP's machine-readable code when the server sent one, otherwise one
    /// of the local codes <c>timeout</c>, <c>closed</c>, <c>write_failed</c>, or <c>stale_cursor</c> (paged history
    /// changed while it was being read).
    /// </summary>
    public sealed class OmpRequestException : Exception
    {
        public OmpRequestException(string message, string command, string? code = null, Exception? inner = null)
            : base(message, inner)
        {
            Command = command;
            Code = code;
        }

        public string Command { get; }

        public string? Code { get; }
    }

    /// <summary>A start or restart was cancelled by a later restart or stop; it is not a failure to report.</summary>
    public sealed class OmpSupersededException : OperationCanceledException
    {
        public OmpSupersededException(Exception? inner = null)
            : base("OMP start was superseded by a restart or stop", inner)
        {
        }
    }
}
