namespace Omp.Core;

/// <summary>What a host tool returns to the agent; <see cref="IsError"/> reports the text as a tool failure.</summary>
public sealed class HostToolResult
{
    /// <summary>
    /// Initializes a new instance of the HostToolResult class with the specified content text and error status.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="isError">The is error.</param>
    private HostToolResult(string text, bool isError)
    {
        Content = text;
        IsError = isError;
    }

    /// <summary>
    /// Gets the content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets a value indicating whether is error.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Creates a HostToolResult instance containing the specified text and indicating that no error occurred.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The host tool result result.</returns>
    public static HostToolResult Text(string text) => new HostToolResult(text, false);

    /// <summary>
    /// Creates a HostToolResult instance that indicates a failure state with the specified error message.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The host tool result result.</returns>
    public static HostToolResult Error(string text) => new HostToolResult(text, true);
}
