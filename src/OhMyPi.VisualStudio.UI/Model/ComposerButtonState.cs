namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>The composer's single action button.</summary>
internal sealed class ComposerButtonState
{
    /// <summary>Stop while the agent works, otherwise Send.</summary>
    public bool IsStop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enabled.
    /// </summary>
    public bool Enabled { get; set; }
}
