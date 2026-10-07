namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A rendered transcript item that is set off by a rule above it when it answers the tool calls before it.</summary>
internal interface ISeparatedView
{
    void SetSeparated(bool separated);
}
