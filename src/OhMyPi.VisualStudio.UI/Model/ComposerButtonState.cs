namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>The composer's single action button.</summary>
internal sealed class ComposerButtonState
{
    /// <summary>Stop while the agent works, otherwise Send.</summary>
    public bool IsStop { get; set; }

    public bool Enabled { get; set; }
}
