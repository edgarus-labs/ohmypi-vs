namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Something attached to the next prompt; shown as a removable chip above the input.</summary>
internal abstract class Attachment
{
    protected Attachment(string label) => Label = label;

    public string Label { get; }
}
