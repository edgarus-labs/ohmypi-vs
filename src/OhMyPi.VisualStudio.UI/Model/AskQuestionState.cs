using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What the user picked for one ask question.</summary>
internal sealed class AskQuestionState
{
    /// <summary>
    /// Initializes a new instance of the AskQuestionState class with the specified selected options and custom input.
    /// </summary>
    /// <param name="selected">The collection of selected.</param>
    /// <param name="custom">The custom.</param>
    public AskQuestionState(IReadOnlyList<string> selected, string custom)
    {
        Selected = selected;
        Custom = custom;
    }

    /// <summary>
    /// Gets the collection of selected.
    /// </summary>
    public IReadOnlyList<string> Selected { get; }

    /// <summary>
    /// Gets the custom.
    /// </summary>
    public string Custom { get; }
}
