namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What narrows the picker list: the search words and the three filters, which all apply together.</summary>
internal sealed class ModelPickerFilter
{
    /// <summary>
    /// Gets or sets the query.
    /// </summary>
    public string Query { get; set; } = "";

    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether favorites only.
    /// </summary>
    public bool FavoritesOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether recent only.
    /// </summary>
    public bool RecentOnly { get; set; }
}
