namespace Omp.Core;

public sealed class AssistantItem : TranscriptItem
{
    public string Text { get; set; } = "";

    public string Thinking { get; set; } = "";

    public bool Streaming { get; set; }

    public string? Model { get; set; }

    public string? StopReason { get; set; }

    public string? ErrorMessage { get; set; }

    public UsageView? Usage { get; set; }
}
