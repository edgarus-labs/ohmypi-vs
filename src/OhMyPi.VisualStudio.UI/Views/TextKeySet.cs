namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Represents a collection of text styling keys used to define foreground, muted, and subtle visual states.
/// </summary>
internal sealed class TextKeySet
{
    /// <summary>
    /// Initializes a new instance of the TextKeySet class with the specified foreground, muted, and subtle color values.
    /// </summary>
    /// <param name="foreground">The foreground.</param>
    /// <param name="muted">The muted.</param>
    /// <param name="subtle">The subtle.</param>
    public TextKeySet(object foreground, object muted, object subtle)
    {
        Foreground = foreground;
        Muted = muted;
        Subtle = subtle;
    }

    /// <summary>
    /// Gets the foreground.
    /// </summary>
    public object Foreground { get; }

    /// <summary>
    /// Gets the muted.
    /// </summary>
    public object Muted { get; }

    /// <summary>
    /// Gets the subtle.
    /// </summary>
    public object Subtle { get; }
}
