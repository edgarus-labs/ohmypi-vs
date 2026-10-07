namespace Omp.Core;

/// <summary>What a host tool returns to the agent; <see cref="IsError"/> reports the text as a tool failure.</summary>
public sealed class HostToolResult
{
    private HostToolResult(string text, bool isError)
    {
        Content = text;
        IsError = isError;
    }

    public string Content { get; }

    public bool IsError { get; }

    public static HostToolResult Text(string text) => new HostToolResult(text, false);

    public static HostToolResult Error(string text) => new HostToolResult(text, true);
}
