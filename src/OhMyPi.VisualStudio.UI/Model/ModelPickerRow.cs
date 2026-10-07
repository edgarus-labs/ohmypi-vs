namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A provider node or a model of the picker tree, with the state the row shows.</summary>
internal sealed class ModelPickerRow
{
    private ModelPickerRow(string provider, int count, bool isExpanded, ModelEntry? entry, bool isActive, bool isFavorite)
    {
        Provider = provider;
        Count = count;
        IsExpanded = isExpanded;
        Entry = entry;
        IsActive = isActive;
        IsFavorite = isFavorite;
    }

    public static ModelPickerRow Group(string provider, int count, bool isExpanded) => new ModelPickerRow(provider, count, isExpanded, null, false, false);

    public static ModelPickerRow Model(ModelEntry entry, bool isActive, bool isFavorite) => new ModelPickerRow(entry.Provider, 0, false, entry, isActive, isFavorite);

    public bool IsGroup => Entry is null;

    public string Provider { get; }

    /// <summary>For a provider node: how many of its models the current search and filters leave.</summary>
    public int Count { get; }

    /// <summary>For a provider node: whether its models are listed under it.</summary>
    public bool IsExpanded { get; }

    public ModelEntry? Entry { get; }

    /// <summary>The model the conversation uses now.</summary>
    public bool IsActive { get; }

    public bool IsFavorite { get; }
}
