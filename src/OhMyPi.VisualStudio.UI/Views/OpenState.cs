using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Expand state of collapsible sections by key, kept across re-renders of the same item.</summary>
internal sealed class OpenState
{
    private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>(StringComparer.Ordinal);

    public bool? IsOpen(string key) => _open.TryGetValue(key, out var open) ? open : (bool?)null;

    public void SetOpen(string key, bool open) => _open[key] = open;

    /// <summary>Forgets every remembered state; item ids of another session must not inherit them.</summary>
    public void Clear() => _open.Clear();
}
