using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>What the user picked for one ask question.</summary>
internal sealed class AskQuestionState
{
    public AskQuestionState(IReadOnlyList<string> selected, string custom)
    {
        Selected = selected;
        Custom = custom;
    }

    public IReadOnlyList<string> Selected { get; }

    public string Custom { get; }
}
