namespace Omp.Core;

/// <summary>
/// Represents the current state and diagnostic details of a network connection, including the process identifier and protocol version.
/// </summary>
public sealed class ConnectionStatus
{
    /// <summary>
    /// Gets or sets the state.
    /// </summary>
    public ConnectionState State { get; set; }

    /// <summary>
    /// Gets or sets the detail.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets the pid.
    /// </summary>
    public int? Pid { get; set; }

    /// <summary>
    /// Gets or sets the protocol version.
    /// </summary>
    public int? ProtocolVersion { get; set; }
}
