namespace Omp.Core;

/// <summary>
/// Represents a transcript item containing user-specific text and the count of associated images.
/// </summary>
public sealed class UserItem : TranscriptItem
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Gets or sets the image count.
    /// </summary>
    public int ImageCount { get; set; }
}
