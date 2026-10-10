using OhMyPi.VisualStudio.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>The keys of closed items, newest first, written through to <see cref="IPreferenceStore"/> and capped at <see cref="Limit"/>.</summary>
internal sealed class DismissalStore : IDismissals
{
    /// <summary>
    /// The limit.
    /// </summary>
    internal const int Limit = 100;
    /// <summary>
    /// The key.
    /// </summary>
    private const string Key = "Dismissed";

    private readonly IPreferenceStore _store;
    private List<string>? _keys;

    /// <summary>
    /// Initializes a new instance of the DismissalStore class using the specified preference store.
    /// </summary>
    /// <param name="store">The store.</param>
    public DismissalStore(IPreferenceStore store) => _store = store;

    /// <summary>
    /// Determines whether the specified key exists within the collection of dismissed items using an ordinal case-sensitive comparison.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>true if the condition is met; otherwise, false.</returns>
    public bool IsDismissed(string key) => Keys().Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// Dismisses the specified key by moving it to the top of the priority list and persisting the updated collection to the store while enforcing the defined limit.
    /// </summary>
    /// <param name="key">The key.</param>
    public void Dismiss(string key)
    {
        var keys = Keys();
        keys.Remove(key);
        keys.Insert(0, key);
        if (keys.Count > Limit)
        {
            keys.RemoveRange(Limit, keys.Count - Limit);
        }

        _store.Write(Key, string.Join("\n", keys));
    }

    /// <summary>
    /// Retrieves the list of keys from the store, lazily initializing and caching the result by splitting the stored value by newline characters.
    /// </summary>
    /// <returns>A collection of list items.</returns>
    private List<string> Keys() =>
        _keys ??= [.. (_store.Read(Key) ?? "").Split(['\n'], StringSplitOptions.RemoveEmptyEntries)];
}
