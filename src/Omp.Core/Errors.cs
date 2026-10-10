using System;

namespace Omp.Core;

/// <summary>
/// An OMP command failed. <see cref="Code"/> is OMP's machine-readable code when the server sent one, otherwise one
/// of the local codes <c>timeout</c>, <c>closed</c>, <c>write_failed</c>, or <c>stale_cursor</c> (paged history
/// changed while it was being read).
/// </summary>
public sealed class OmpRequestException : Exception
{
    /// <summary>
    /// Initializes a new instance of the OmpRequestException class with the specified error message, command identifier, optional error code, and inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="command">The command containing the operation data.</param>
    /// <param name="code">The code.</param>
    /// <param name="inner">The inner.</param>
    public OmpRequestException(string message, string command, string? code = null, Exception? inner = null)
        : base(message, inner)
    {
        Command = command;
        Code = code;
    }

    /// <summary>
    /// Gets the command.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the code.
    /// </summary>
    public string? Code { get; }
}
