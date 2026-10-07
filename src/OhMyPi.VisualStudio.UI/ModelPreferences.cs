using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI
{
    /// <summary>A model's identity: the provider OMP lists it under and its technical id there.</summary>
    public sealed class ModelKey : IEquatable<ModelKey>
    {
        public ModelKey(string provider, string id)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Provider { get; }
        public string Id { get; }

        public bool Equals(ModelKey? other) => other != null && Provider == other.Provider && Id == other.Id;

        public override bool Equals(object? obj) => Equals(obj as ModelKey);

        public override int GetHashCode() => unchecked(Provider.GetHashCode() * 397 ^ Id.GetHashCode());

        public override string ToString() => Provider + "/" + Id;
    }

    /// <summary>The models the user starred and picked most recently, kept across Visual Studio sessions.</summary>
    public interface IModelPreferences
    {
        IReadOnlyList<ModelKey> Favorites { get; }

        /// <summary>Models picked before, most recent first.</summary>
        IReadOnlyList<ModelKey> Recents { get; }

        void SetFavorite(ModelKey model, bool favorite);

        /// <summary>Moves <paramref name="model"/> to the front of <see cref="Recents"/>.</summary>
        void RecordPicked(ModelKey model);
    }

    /// <summary>
    /// The model picker's Favorites and Recent filters, kept across Visual Studio sessions. Model preferences that offer
    /// it make the picker open with the filters the user left on.
    /// </summary>
    public interface IModelPickerFilters
    {
        bool FavoritesOnly { get; set; }

        bool RecentOnly { get; set; }
    }
}
