using OhMyPi.VisualStudio.UI.Model;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A row of the model list as the row template binds it: a provider header or a model.</summary>
internal sealed class ModelRowItem : INotifyPropertyChanged
{
    private bool _isFavorite;

    /// <summary>
    /// Initializes a new instance of the ModelRowItem class by mapping the properties and calculating the formatted price and context from the specified ModelPickerRow.
    /// </summary>
    /// <param name="row">The row.</param>
    public ModelRowItem(ModelPickerRow row)
    {
        Entry = row.Entry;
        Provider = row.Provider;
        IsExpanded = row.IsExpanded;
        CountText = row.IsGroup ? row.Count.ToString(CultureInfo.CurrentCulture) : "";
        IsActive = row.IsActive;
        _isFavorite = row.IsFavorite;
        if (Entry is not null)
        {
            var details = ModelDetails.From(Entry.Model);
            Context = details.ContextCompact ?? "";
            Price = details.InputCost == ModelDetails.NotProvided && details.OutputCost == ModelDetails.NotProvided
                ? ""
                : $"{Cost(details.InputCost)} / {Cost(details.OutputCost)}";
        }
    }

    /// <summary>
    /// Occurs when property changed.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the entry.
    /// </summary>
    public ModelEntry? Entry { get; }

    /// <summary>
    /// Gets a value indicating whether is group.
    /// </summary>
    public bool IsGroup => Entry is null;

    /// <summary>
    /// Gets the provider.
    /// </summary>
    public string Provider { get; }

    /// <summary>The provider name in capitals with hair spaces between the letters, standing in for letter spacing.</summary>
    public string ProviderCaps => string.Join("\u200A", Provider.ToUpperInvariant().Select(c => c.ToString()));

    /// <summary>
    /// Gets a value indicating whether is expanded.
    /// </summary>
    public bool IsExpanded { get; }

    /// <summary>
    /// Gets the count text.
    /// </summary>
    public string CountText { get; }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => Entry?.Name ?? "";

    /// <summary>
    /// Gets the id.
    /// </summary>
    public string Id => Entry?.Id ?? "";

    /// <summary>The id is shown under the name only when another model of the provider has the same name.</summary>
    public bool ShowId => Entry?.Variant is not null;

    /// <summary>The context window as "1M" or "200k"; empty when the catalog does not give it.</summary>
    public string Context { get; } = "";

    /// <summary>
    /// Gets a value indicating whether has context.
    /// </summary>
    public bool HasContext => Context.Length > 0;

    /// <summary>Input and output price per million tokens as "$3 / $15"; empty when the catalog gives neither.</summary>
    public string Price { get; } = "";

    /// <summary>The model the conversation uses now.</summary>
    public bool IsActive { get; }

    /// <summary>
    /// Gets or sets a value indicating whether is favorite.
    /// </summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            FavoriteChanged?.Invoke(this);
        }
    }

    /// <summary>Raised when the favorite flag was changed by the user.</summary>
    public event Action<ModelRowItem>? FavoriteChanged;

    /// <summary>
    /// Gets the automation name.
    /// </summary>
    public string AutomationName => IsGroup
        ? $"{Provider}, {CountText} models, {(IsExpanded ? "expanded" : "collapsed")}"
        : $"{Name}, {Provider}, {Id}{(IsActive ? ", current model" : "")}{(IsFavorite ? ", favorite" : "")}";

    /// <summary>
    /// Formats the cost string by returning &quot;n/a&quot; if the value is not provided.
    /// </summary>
    /// <param name="cost">The cost.</param>
    /// <returns>The string result.</returns>
    private static string Cost(string cost) => cost == ModelDetails.NotProvided ? "n/a" : cost;
}
