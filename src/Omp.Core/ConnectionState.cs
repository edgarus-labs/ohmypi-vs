namespace Omp.Core;

/// <summary>
/// Specifies the current operational state of a connection, including its initialization, readiness, and failure status.
/// </summary>
public enum ConnectionState { Stopped, Starting, Ready, Restarting, Failed }
