namespace OhMyPi.VisualStudio.UI.Views;

internal sealed class TextKeySet
{
    public TextKeySet(object foreground, object muted, object subtle)
    {
        Foreground = foreground;
        Muted = muted;
        Subtle = subtle;
    }

    public object Foreground { get; }

    public object Muted { get; }

    public object Subtle { get; }
}
