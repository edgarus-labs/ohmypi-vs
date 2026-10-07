namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class ProviderOption
{
    public ProviderOption(string name, int count)
    {
        Name = name;
        Count = count;
    }

    public string Name { get; }

    public int Count { get; }
}
