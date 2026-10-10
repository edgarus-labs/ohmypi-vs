using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio;

/// <summary>Keeps preferences as strings in one collection of the user settings store; UI thread only.</summary>
internal sealed class SettingsPreferenceStore : IPreferenceStore
{
    private readonly WritableSettingsStore _store;
    private readonly string _collection;

    /// <summary>
    /// Initializes a new instance of the SettingsPreferenceStore class using the specified writable settings store and collection identifier.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="collection">The collection.</param>
    public SettingsPreferenceStore(WritableSettingsStore store, string collection)
    {
        _store = store;
        _collection = collection;
    }

    /// <summary>
    /// Retrieves the string value associated with the specified key from the current collection, returning null if the collection does not exist.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The string? result.</returns>
    public string? Read(string key)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return _store.CollectionExists(_collection) ? _store.GetString(_collection, key, "") : null;
    }

    /// <summary>
    /// Writes a string value associated with the specified key to the designated collection, ensuring the collection exists and the operation is executed on the UI thread.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public void Write(string key, string value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_store.CollectionExists(_collection))
        {
            _store.CreateCollection(_collection);
        }

        _store.SetString(_collection, key, value);
    }
}
