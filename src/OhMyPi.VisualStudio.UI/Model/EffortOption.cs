namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class EffortOption
{
    public EffortOption(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }

    public string Label { get; }
}
