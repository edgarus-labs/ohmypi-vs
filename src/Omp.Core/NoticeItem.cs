namespace Omp.Core;

/// <summary>
/// Represents a notification entry within a transcript, containing the notice level and the associated descriptive text.
/// </summary>
public sealed class NoticeItem : TranscriptItem
{
    /// <summary>
    /// Gets or sets the level.
    /// </summary>
    public NoticeLevel Level { get; set; }

    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";
}
