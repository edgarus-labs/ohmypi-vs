namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a configuration option for a provider, specifying its name and associated count.
/// </summary>
internal sealed class ProviderOption
{
    /// <summary>
    /// Initializes a new instance of the ProviderOption class with the specified name and count.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="count">The count.</param>
    public ProviderOption(string name, int count)
    {
        Name = name;
        Count = count;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the count.
    /// </summary>
    public int Count { get; }
}
