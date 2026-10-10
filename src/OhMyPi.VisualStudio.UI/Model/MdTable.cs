using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a Markdown table block containing a header, column alignments, and a collection of data rows.
/// </summary>
internal sealed class MdTable : MdBlock
{
    /// <summary>
    /// Initializes a new instance of the MdTable class with the specified header, column alignments, and row data.
    /// </summary>
    /// <param name="header">The collection of header.</param>
    /// <param name="aligns">The collection of aligns.</param>
    /// <param name="rows">The collection of rows.</param>
    public MdTable(IReadOnlyList<IReadOnlyList<MdInline>> header, IReadOnlyList<MdAlign> aligns, IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> rows)
    {
        Header = header;
        Aligns = aligns;
        Rows = rows;
    }

    /// <summary>
    /// Gets the collection of header.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<MdInline>> Header { get; }

    /// <summary>
    /// Gets the collection of aligns.
    /// </summary>
    public IReadOnlyList<MdAlign> Aligns { get; }

    /// <summary>
    /// Gets the collection of rows.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows { get; }
}
