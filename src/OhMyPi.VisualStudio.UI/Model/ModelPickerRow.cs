namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>A provider node or a model of the picker tree, with the state the row shows.</summary>
internal sealed class ModelPickerRow
{
    /// <summary>
    /// Initializes a new instance of the ModelPickerRow class with the specified provider, count, expansion state, model entry, and status flags.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="count">The count.</param>
    /// <param name="isExpanded">The is expanded.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="isActive">The is active.</param>
    /// <param name="isFavorite">The is favorite.</param>
    private ModelPickerRow(string provider, int count, bool isExpanded, ModelEntry? entry, bool isActive, bool isFavorite)
    {
        Provider = provider;
        Count = count;
        IsExpanded = isExpanded;
        Entry = entry;
        IsActive = isActive;
        IsFavorite = isFavorite;
    }

    /// <summary>
    /// Creates a new ModelPickerRow representing a grouped collection of models for the specified provider.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="count">The count.</param>
    /// <param name="isExpanded">The is expanded.</param>
    /// <returns>The model picker row result.</returns>
    public static ModelPickerRow Group(string provider, int count, bool isExpanded) => new ModelPickerRow(provider, count, isExpanded, null, false, false);

    /// <summary>
    /// Creates a new ModelPickerRow instance based on the provided model entry, active status, and favorite designation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="isActive">The is active.</param>
    /// <param name="isFavorite">The is favorite.</param>
    /// <returns>The model picker row result.</returns>
    public static ModelPickerRow Model(ModelEntry entry, bool isActive, bool isFavorite) => new ModelPickerRow(entry.Provider, 0, false, entry, isActive, isFavorite);

    /// <summary>
    /// Gets a value indicating whether is group.
    /// </summary>
    public bool IsGroup => Entry is null;

    /// <summary>
    /// Gets the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>For a provider node: how many of its models the current search and filters leave.</summary>
    public int Count { get; }

    /// <summary>For a provider node: whether its models are listed under it.</summary>
    public bool IsExpanded { get; }

    /// <summary>
    /// Gets the entry.
    /// </summary>
    public ModelEntry? Entry { get; }

    /// <summary>The model the conversation uses now.</summary>
    public bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether is favorite.
    /// </summary>
    public bool IsFavorite { get; }
}
