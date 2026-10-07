namespace Omp.Core;

public sealed class SessionSummary
{
    public string Path { get; set; } = "";

    public string? Id { get; set; }

    public string? Title { get; set; }

    public string? FirstMessage { get; set; }

    public string? Cwd { get; set; }

    /// <summary>Unix milliseconds.</summary>
    public long Modified { get; set; }

    public long Size { get; set; }
}
