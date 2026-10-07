namespace Omp.Core;

public sealed class ConnectionStatus
{
    public ConnectionState State { get; set; }

    public string? Detail { get; set; }

    public int? Pid { get; set; }

    public int? ProtocolVersion { get; set; }
}
