using OhMyPi.VisualStudio.Logic;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class DismissalStoreTests
{
    private sealed class FakeStore : IPreferenceStore
    {
        public Dictionary<string, string> Values { get; } = new();

        public string? Read(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public void Write(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void ADismissedKeyStaysDismissedAcrossStoresOverTheSameData()
    {
        var data = new FakeStore();
        var store = new DismissalStore(data);
        Assert.False(store.IsDismissed("todos:1"));
        store.Dismiss("todos:1");
        store.Dismiss("todos:2");
        store.Dismiss("todos:1");
        Assert.True(store.IsDismissed("todos:1"));
        Assert.True(new DismissalStore(data).IsDismissed("todos:2"));
        Assert.False(new DismissalStore(data).IsDismissed("todos:3"));
        Assert.Equal("todos:1\ntodos:2", data.Values["Dismissed"]);
    }

    [Fact]
    public void OnlyTheNewestKeysAreKept()
    {
        var data = new FakeStore();
        var store = new DismissalStore(data);
        for (var i = 0; i < DismissalStore.Limit + 5; i++)
        {
            store.Dismiss("k" + i);
        }

        Assert.False(store.IsDismissed("k0"));
        Assert.True(store.IsDismissed("k" + (DismissalStore.Limit + 4)));
        Assert.Equal(DismissalStore.Limit, data.Values["Dismissed"].Split('\n').Length);
    }
}
