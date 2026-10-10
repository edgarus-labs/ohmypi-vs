namespace OhMyPi.VisualStudio.UI;

/// <summary>Why no OMP service could be built.</summary>
public sealed class OmpUnavailable
{
    /// <summary>
    /// Initializes a new instance of the OmpUnavailable class with a specified error message and a flag indicating whether the executable was not found.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="executableNotFound">The executable not found.</param>
    public OmpUnavailable(string message, bool executableNotFound)
    {
        Message = message;
        ExecutableNotFound = executableNotFound;
    }

    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message { get; }

    /// <summary>True when the OMP executable could not be located; false when building the service failed otherwise.</summary>
    public bool ExecutableNotFound { get; }
}
