using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class MdTable : MdBlock
{
    public MdTable(IReadOnlyList<IReadOnlyList<MdInline>> header, IReadOnlyList<MdAlign> aligns, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> rows)
    {
        Header = header;
        Aligns = aligns;
        Rows = rows;
    }

    public IReadOnlyList<IReadOnlyList<MdInline>> Header { get; }

    public IReadOnlyList<MdAlign> Aligns { get; }

    public IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows { get; }
}
