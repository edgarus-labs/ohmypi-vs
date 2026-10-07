using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class MdListItem
{
    public string Raw { get; set; } = "";

    public IReadOnlyList<MdInline> Inlines { get; set; } = Array.Empty<MdInline>();

    public List<MdList> Children { get; } = new List<MdList>();
}
