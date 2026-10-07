using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OhMyPi.VisualStudio.UI;

namespace OhMyPi.VisualStudio.Logic
{
    /// <summary>String values the model preferences are persisted in.</summary>
    internal interface IPreferenceStore
    {
        string? Read(string key);

        void Write(string key, string value);
    }

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

        public IReadOnlyList<ModelKey> Favorites
        {
            get
            {
                lock (_gate) return Favorited().ToArray();
            }
        }

        public IReadOnlyList<ModelKey> Recents
        {
            get
            {
                lock (_gate) return Recent().ToArray();
            }
        }

        public void SetFavorite(ModelKey model, bool favorite)
        {
            lock (_gate)
            {
                var favorites = Favorited();
                if (favorite ? favorites.Contains(model) : !favorites.Remove(model)) return;
                if (favorite) favorites.Add(model);
                _store.Write(FavoritesKey, Encode(favorites));
            }
        }

        public void RecordPicked(ModelKey model)
        {
            lock (_gate)
            {
                var recents = Recent();
                recents.Remove(model);
                recents.Insert(0, model);
                if (recents.Count > RecentLimit) recents.RemoveRange(RecentLimit, recents.Count - RecentLimit);
                _store.Write(RecentsKey, Encode(recents));
            }
        }

        public bool FavoritesOnly
        {
            get => _store.Read(FavoritesOnlyKey) == "1";
            set => _store.Write(FavoritesOnlyKey, value ? "1" : "0");
        }

        public bool RecentOnly
        {
            get => _store.Read(RecentOnlyKey) == "1";
            set => _store.Write(RecentOnlyKey, value ? "1" : "0");
        }

        private List<ModelKey> Favorited() => _favorites ??= Decode(_store.Read(FavoritesKey));

        private List<ModelKey> Recent() => _recents ??= Decode(_store.Read(RecentsKey));

        /// <summary>Entries are <c>provider/id</c> with both parts percent-escaped, separated by semicolons.</summary>
        private static string Encode(IEnumerable<ModelKey> keys) =>
            string.Join(";", keys.Select(k => Uri.EscapeDataString(k.Provider) + "/" + Uri.EscapeDataString(k.Id)));

        private static List<ModelKey> Decode(string? text)
        {
            var keys = new List<ModelKey>();
            foreach (var entry in (text ?? "").Split(';'))
            {
                var parts = entry.Split('/');
                if (parts.Length != 2 || !Escaped.IsMatch(parts[0]) || !Escaped.IsMatch(parts[1])) continue;
                var key = new ModelKey(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
                if (!keys.Contains(key)) keys.Add(key);
            }
            return keys;
        }
    }
}
