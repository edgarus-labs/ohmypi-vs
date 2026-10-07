using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class ImageAdmission
{
    public ImageAdmission(IReadOnlyList<int> accepted, IReadOnlyList<string> rejected)
    {
        Accepted = accepted;
        Rejected = rejected;
    }

    /// <summary>Indexes of the admitted files.</summary>
    public IReadOnlyList<int> Accepted { get; }

    /// <summary>One message per rejected file.</summary>
    public IReadOnlyList<string> Rejected { get; }
}
