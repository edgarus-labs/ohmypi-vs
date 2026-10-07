namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Follows new output only while the reader is at the bottom: scrolling up stops following and offers
/// "jump to latest"; returning to the bottom resumes. Only the reader's own scrolling (<see cref="ReaderScrolling"/>)
/// turns following off or on; the view moved by layout (content measured, zoom, focus) keeps whatever was chosen.
/// </summary>
internal sealed class FollowBottom
{
    private const double Threshold = 24;

    public bool Stick { get; private set; } = true;

    public bool JumpVisible { get; private set; }

    /// <summary>True while the reader drives the scroll (wheel, keys, scroll bar); other offset changes come from layout.</summary>
    public bool ReaderScrolling { get; set; }

    public static bool IsAtBottom(double extentHeight, double offset, double viewportHeight) =>
        extentHeight - offset - viewportHeight <= Threshold;

    /// <summary>
    /// Feeds one scroll-changed notification. Returns true when the view should scroll to the bottom.
    /// An offset change the reader made decides whether to follow; any other offset change, and a pure extent or
    /// viewport change, is layout, which brings a following view back to the bottom.
    /// </summary>
    public bool OnScrollChanged(double extentHeight, double offset, double viewportHeight, double extentChange, double viewportChange, double offsetChange)
    {
        if (offsetChange != 0)
        {
            if (ReaderScrolling)
            {
                Stick = IsAtBottom(extentHeight, offset, viewportHeight);
                JumpVisible = !Stick;

                return false;
            }
            if (!Stick)
            {
                return false;
            }

            JumpVisible = false;

            return !IsAtBottom(extentHeight, offset, viewportHeight);
        }
        if (extentChange == 0 && viewportChange == 0)
        {
            return false;
        }

        if (Stick)
        {
            return true;
        }

        JumpVisible = true;

        return false;
    }

    /// <summary>Sticks to the bottom again (jump button, sent prompt).</summary>
    public void ToBottom()
    {
        Stick = true;
        JumpVisible = false;
    }

    /// <summary>Stops following before a keyboard scroll up, so content measured while it scrolls cannot pull the view back down.</summary>
    public void Release()
    {
        Stick = false;
        JumpVisible = true;
    }
}
