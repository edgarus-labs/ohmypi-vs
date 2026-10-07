using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio
{
    /// <summary>Keeps preferences as strings in one collection of the user settings store; UI thread only.</summary>
    internal sealed class SettingsPreferenceStore : IPreferenceStore
    {
        private readonly WritableSettingsStore _store;
        private readonly string _collection;

        public SettingsPreferenceStore(WritableSettingsStore store, string collection)
        {
            _store = store;
            _collection = collection;
        }

        public string? Read(string key)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _store.CollectionExists(_collection) ? _store.GetString(_collection, key, "") : null;
        }

        public void Write(string key, string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_store.CollectionExists(_collection)) _store.CreateCollection(_collection);
            _store.SetString(_collection, key, value);
        }
    }
}
