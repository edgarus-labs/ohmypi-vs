namespace Omp.Core.Changes;

/// <summary>A file named by a tool result's <c>details</c>, with the content it had before the tool ran when reported.</summary>
public sealed class ResultFile
{
    public ResultFile(string path, string? oldText)
    {
        Path = path;
        OldText = oldText;
    }

    public string Path { get; }

    public string? OldText { get; set; }
}
