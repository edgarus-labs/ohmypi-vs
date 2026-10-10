using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Expand state of collapsible sections by key, kept across re-renders of the same item.</summary>
internal sealed class OpenState
{
    private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>
    /// Determines whether the specified key is marked as open, returning the state if found or null if the key does not exist.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The bool? result.</returns>
    public bool? IsOpen(string key) => _open.TryGetValue(key, out var open) ? open : (bool?)null;

    /// <summary>
    /// Sets the open state for the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="open">The open.</param>
    public void SetOpen(string key, bool open) => _open[key] = open;

    /// <summary>Forgets every remembered state; item ids of another session must not inherit them.</summary>
    public void Clear() => _open.Clear();
}
