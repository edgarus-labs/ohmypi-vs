using OhMyPi.VisualStudio.Logic;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class FilterPreferencesTests
{
    private sealed class FakeStore : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new();

        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

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
