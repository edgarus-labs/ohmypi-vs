namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What narrows the picker list: the search words and the three filters, which all apply together.</summary>
internal sealed class ModelPickerFilter
{
    public string Query { get; set; } = "";

    public string? Provider { get; set; }

    public bool FavoritesOnly { get; set; }

    public bool RecentOnly { get; set; }
}
