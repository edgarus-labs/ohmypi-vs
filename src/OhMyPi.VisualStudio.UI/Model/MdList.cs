using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class MdList : MdBlock
{
    public MdList(bool ordered, int start)
    {
        Ordered = ordered;
        Start = start;
    }

    public bool Ordered { get; }

    public int Start { get; }

    public List<MdListItem> Items { get; } = new List<MdListItem>();
}
