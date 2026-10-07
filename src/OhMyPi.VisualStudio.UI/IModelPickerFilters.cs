namespace OhMyPi.VisualStudio.UI;

/// <summary>
/// The model picker's Favorites and Recent filters, kept across Visual Studio sessions. Model preferences that offer
/// it make the picker open with the filters the user left on.
/// </summary>
public interface IModelPickerFilters
{
    bool FavoritesOnly { get; set; }

    bool RecentOnly { get; set; }
}
