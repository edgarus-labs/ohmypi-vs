using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.Tests;

public sealed class ModelPreferenceStoreTests
{
    /// <summary>
    /// Represents an in-memory implementation of the preference store used for testing and mocking preference persistence operations.
    /// </summary>
    private sealed class FakeStore : IPreferenceStore
    {
        /// <summary>
        /// Gets the values.
        /// </summary>
        public Dictionary<string, string> Values { get; } = new();

        /// <summary>
        /// Gets or sets the writes.
        /// </summary>
        public int Writes { get; private set; }

        /// <summary>
        /// Retrieves the value associated with the specified key if it exists; otherwise, returns null.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The string? result.</returns>
        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        /// <summary>
        /// Writes the specified value to the collection associated with the given key and increments the write operation counter.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Write(string key, string value)
        {
            Writes++;
            Values[key] = value;
        }
    }

    private static ModelKey Key(string provider, string id) => new(provider, id);

    private static string[] Ids(IReadOnlyList<ModelKey> keys) => [.. keys.Select(k => k.ToString())];

    private readonly FakeStore _store = new();

    [Fact]
    public void StartsEmptyWhenNothingWasStored()
    {
        var preferences = new ModelPreferenceStore(_store);
        Assert.Empty(preferences.Favorites);
        Assert.Empty(preferences.Recents);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public void FavoritesRoundTripThroughTheStore()
    {
        var first = new ModelPreferenceStore(_store);
        first.SetFavorite(Key("anthropic", "sonnet"), true);
        first.SetFavorite(Key("openai", "gpt"), true);
        first.SetFavorite(Key("anthropic", "sonnet"), false);

        var second = new ModelPreferenceStore(_store);
        Assert.Equal(new[] { "openai/gpt" }, Ids(second.Favorites));
    }

    [Fact]
    public void StarringTwiceOrUnstarringAStrangerWritesNothing()
    {
        var preferences = new ModelPreferenceStore(_store);
        preferences.SetFavorite(Key("a", "b"), true);
        var writes = _store.Writes;
        preferences.SetFavorite(Key("a", "b"), true);
        preferences.SetFavorite(Key("x", "y"), false);
        Assert.Equal(writes, _store.Writes);
    }

    [Fact]
    public void RecentsKeepTheMostRecentPickFirstWithoutDuplicates()
    {
        var first = new ModelPreferenceStore(_store);
        first.RecordPicked(Key("a", "1"));
        first.RecordPicked(Key("a", "2"));
        first.RecordPicked(Key("b", "1"));
        first.RecordPicked(Key("a", "1"));

        Assert.Equal(new[] { "a/1", "b/1", "a/2" }, Ids(first.Recents));
        Assert.Equal(new[] { "a/1", "b/1", "a/2" }, Ids(new ModelPreferenceStore(_store).Recents));
    }

    [Fact]
    public void RecentsAreCappedAtTwentyDroppingTheOldest()
    {
        var preferences = new ModelPreferenceStore(_store);
        for (var i = 1; i <= 25; i++)
        {
            preferences.RecordPicked(Key("p", "m" + i));
        }

        Assert.Equal(20, preferences.Recents.Count);
        Assert.Equal("p/m25", preferences.Recents[0].ToString());
        Assert.Equal("p/m6", preferences.Recents[19].ToString());
    }

    [Fact]
    public void IdsWithSeparatorsAndNonAsciiRoundTrip()
    {
        var odd = Key("open;router", "anthropic/claude-3.5:beta ; zażółć\n,");
        new ModelPreferenceStore(_store).SetFavorite(odd, true);
        Assert.Equal(new[] { odd }, new ModelPreferenceStore(_store).Favorites);
    }

    [Fact]
    public void FavoritesAndRecentsAreStoredSeparately()
    {
        var preferences = new ModelPreferenceStore(_store);
        preferences.SetFavorite(Key("a", "fav"), true);
        preferences.RecordPicked(Key("a", "picked"));

        var reloaded = new ModelPreferenceStore(_store);
        Assert.Equal(new[] { "a/fav" }, Ids(reloaded.Favorites));
        Assert.Equal(new[] { "a/picked" }, Ids(reloaded.Recents));
    }

    [Fact]
    public void UnreadableEntriesAreSkippedAndTheRestKept()
    {
        var probe = new ModelPreferenceStore(_store);
        probe.SetFavorite(Key("good", "one"), true);
        var stored = _store.Values.Single().Value;
        _store.Values[_store.Values.Single().Key] = "garbage;;no-slash;" + stored + ";%zz/%zz";

        Assert.Equal(new[] { "good/one" }, Ids(new ModelPreferenceStore(_store).Favorites));
    }
}
