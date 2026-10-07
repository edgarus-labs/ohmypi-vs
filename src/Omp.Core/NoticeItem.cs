namespace Omp.Core;

public sealed class NoticeItem : TranscriptItem
{
    public NoticeLevel Level { get; set; }

    public string Text { get; set; } = "";
}
