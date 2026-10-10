using OhMyPi.VisualStudio.Logic;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class FilterPreferencesTests
{
    /// <summary>
    /// Represents an in-memory implementation of the preference store used for testing or mocking purposes.
    /// </summary>
    private sealed class FakeStore : IPreferenceStore
    {
        /// <summary>
        /// Gets the values.
        /// </summary>
        public Dictionary<string, string> Values { get; } = new();

        /// <summary>
        /// Retrieves the value associated with the specified key if it exists; otherwise, returns null.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The string? result.</returns>
        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        /// <summary>
        /// Writes the specified value to the collection, associating it with the provided key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Write(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void TheFavoritesAndRecentFiltersAreRememberedSeparately()
    {
        var store = new FakeStore();
        var preferences = new ModelPreferenceStore(store);
        Assert.False(preferences.FavoritesOnly);
        Assert.False(preferences.RecentOnly);
        preferences.FavoritesOnly = true;
        Assert.True(new ModelPreferenceStore(store).FavoritesOnly);
        Assert.False(new ModelPreferenceStore(store).RecentOnly);
        preferences.RecentOnly = true;
        preferences.FavoritesOnly = false;
        var reopened = new ModelPreferenceStore(store);
        Assert.False(reopened.FavoritesOnly);
        Assert.True(reopened.RecentOnly);
    }
}
