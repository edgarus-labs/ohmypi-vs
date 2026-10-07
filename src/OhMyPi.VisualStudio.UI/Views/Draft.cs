using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A prompt the user submitted.</summary>
internal sealed class Draft
{
    public Draft(string text, IReadOnlyList<Attachment> attachments, PromptMode mode)
    {
        Text = text;
        Attachments = attachments;
        Mode = mode;
    }

    public string Text { get; }

    public IReadOnlyList<Attachment> Attachments { get; }

    public PromptMode Mode { get; }
}
