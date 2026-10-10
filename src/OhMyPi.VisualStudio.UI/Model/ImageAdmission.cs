using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents the results of an image admission process, containing lists of accepted image identifiers and rejected image reasons.
/// </summary>
internal sealed class ImageAdmission
{
    /// <summary>
    /// Initializes a new instance of the ImageAdmission class with the specified lists of accepted and rejected image identifiers.
    /// </summary>
    /// <param name="accepted">The collection of accepted.</param>
    /// <param name="rejected">The collection of rejected.</param>
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
