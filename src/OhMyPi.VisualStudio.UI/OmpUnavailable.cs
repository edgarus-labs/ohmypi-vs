namespace OhMyPi.VisualStudio.UI;

/// <summary>Why no OMP service could be built.</summary>
public sealed class OmpUnavailable
{
    public OmpUnavailable(string message, bool executableNotFound)
    {
        Message = message;
        ExecutableNotFound = executableNotFound;
    }

    public string Message { get; }

    /// <summary>True when the OMP executable could not be located; false when building the service failed otherwise.</summary>
    public bool ExecutableNotFound { get; }
}
