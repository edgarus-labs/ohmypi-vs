using OhMyPi.VisualStudio.Logic;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class DismissalStoreTests
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
        /// Writes the specified value to the collection associated with the provided key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
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
