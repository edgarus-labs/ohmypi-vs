using OhMyPi.VisualStudio.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>The keys of closed items, newest first, written through to <see cref="IPreferenceStore"/> and capped at <see cref="Limit"/>.</summary>
internal sealed class DismissalStore : IDismissals
{
    internal const int Limit = 100;
    private const string Key = "Dismissed";

    private readonly IPreferenceStore _store;
    private List<string>? _keys;

    public DismissalStore(IPreferenceStore store) => _store = store;

    public bool IsDismissed(string key) => Keys().Contains(key, StringComparer.Ordinal);

    public void Dismiss(string key)
    {
        var keys = Keys();
        keys.Remove(key);
        keys.Insert(0, key);
        if (keys.Count > Limit)
        {
            keys.RemoveRange(Limit, keys.Count - Limit);
        }

        _store.Write(Key, string.Join("\n", keys));
    }

    private List<string> Keys() =>
        _keys ??= [.. (_store.Read(Key) ?? "").Split(['\n'], StringSplitOptions.RemoveEmptyEntries)];
}
