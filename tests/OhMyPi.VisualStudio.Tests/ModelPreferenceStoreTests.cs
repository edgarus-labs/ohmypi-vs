using System.Collections.Generic;
using System.Linq;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.UI;

namespace OhMyPi.VisualStudio.Tests;

public class ModelPreferenceStoreTests
{
    private sealed class FakeStore : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public int Writes { get; private set; }

        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value)
        {
            Writes++;
            Values[key] = value;
        }
    }

    private static ModelKey Key(string provider, string id) => new(provider, id);

    private static string[] Ids(IReadOnlyList<ModelKey> keys) => keys.Select(k => k.ToString()).ToArray();

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
        for (var i = 1; i <= 25; i++) preferences.RecordPicked(Key("p", "m" + i));

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
