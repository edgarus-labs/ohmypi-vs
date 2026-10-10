using OhMyPi.VisualStudio.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>Favorite and recently picked models and the picker's filters, loaded on first use and written through to <see cref="IPreferenceStore"/> on every change.</summary>
internal sealed class ModelPreferenceStore : IModelPreferences, IModelPickerFilters
{
    internal const int RecentLimit = 20;
    private const string FavoritesKey = "Favorites";
    private const string RecentsKey = "Recents";
    private const string FavoritesOnlyKey = "FavoritesOnly";
    private const string RecentOnlyKey = "RecentOnly";

    private static readonly Regex Escaped = new Regex("^(?:[A-Za-z0-9_.~-]|%[0-9A-F]{2})+$", RegexOptions.CultureInvariant);

    private readonly IPreferenceStore _store;
    private readonly object _gate = new object();
    private List<ModelKey>? _favorites;
    private List<ModelKey>? _recents;

    public ModelPreferenceStore(IPreferenceStore store) => _store = store;

    /// <summary>
    /// Gets the collection of favorites.
    /// </summary>
    public IReadOnlyList<ModelKey> Favorites
    {
        get
        {
            lock (_gate)
            {
                return Favorited().ToArray();
            }
        }
    }

    /// <summary>
    /// Gets the collection of recents.
    /// </summary>
    public IReadOnlyList<ModelKey> Recents
    {
        get
        {
            lock (_gate)
            {
                return Recent().ToArray();
            }
        }
    }

    /// <summary>
    /// Updates the favorite status of the specified model and persists the change to the underlying store.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="favorite">The favorite.</param>
    public void SetFavorite(ModelKey model, bool favorite)
    {
        lock (_gate)
        {
            var favorites = Favorited();
            if (favorite ? favorites.Contains(model) : !favorites.Remove(model))
            {
                return;
            }

            if (favorite)
            {
                favorites.Add(model);
            }

            _store.Write(FavoritesKey, Encode(favorites));
        }
    }

    /// <summary>
    /// Records the specified model as the most recently picked item and persists the updated list to the store while maintaining the defined limit.
    /// </summary>
    /// <param name="model">The model.</param>
    public void RecordPicked(ModelKey model)
    {
        lock (_gate)
        {
            var recents = Recent();
            recents.Remove(model);
            recents.Insert(0, model);
            if (recents.Count > RecentLimit)
            {
                recents.RemoveRange(RecentLimit, recents.Count - RecentLimit);
            }

            _store.Write(RecentsKey, Encode(recents));
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether favorites only.
    /// </summary>
    public bool FavoritesOnly
    {
        get => _store.Read(FavoritesOnlyKey) == "1";
        set => _store.Write(FavoritesOnlyKey, value ? "1" : "0");
    }

    /// <summary>
    /// Gets or sets a value indicating whether recent only.
    /// </summary>
    public bool RecentOnly
    {
        get => _store.Read(RecentOnlyKey) == "1";
        set => _store.Write(RecentOnlyKey, value ? "1" : "0");
    }

    private List<ModelKey> Favorited() => _favorites ??= Decode(_store.Read(FavoritesKey));

    /// <summary>
    /// Retrieves the list of recently accessed model keys, lazily loading and decoding them from the persistent store if they are not already cached.
    /// </summary>
    /// <returns>A collection of list items.</returns>
    private List<ModelKey> Recent() => _recents ??= Decode(_store.Read(RecentsKey));

    /// <summary>Entries are <c>provider/id</c> with both parts percent-escaped, separated by semicolons.</summary>
    private static string Encode(IEnumerable<ModelKey> keys) =>
        string.Join(";", keys.Select(k => Uri.EscapeDataString(k.Provider) + "/" + Uri.EscapeDataString(k.Id)));

    /// <summary>
    /// Parses a semicolon-delimited string of escaped key-value pairs into a list of unique ModelKey instances.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A collection of list items.</returns>
    private static List<ModelKey> Decode(string? text)
    {
        var keys = new List<ModelKey>();
        foreach (var entry in (text ?? "").Split(';'))
        {
            var parts = entry.Split('/');
            if (parts.Length != 2 || !Escaped.IsMatch(parts[0]) || !Escaped.IsMatch(parts[1]))
            {
                continue;
            }

            var key = new ModelKey(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
