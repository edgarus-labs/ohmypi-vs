using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>A prompt the user submitted.</summary>
internal sealed class Draft
{
    /// <summary>
    /// Initializes a new instance of the Draft class with the specified text, attachments, and prompt mode.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="attachments">The collection of attachments.</param>
    /// <param name="mode">The mode.</param>
    public Draft(string text, IReadOnlyList<Attachment> attachments, PromptMode mode)
    {
        Text = text;
        Attachments = attachments;
        Mode = mode;
    }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the collection of attachments.
    /// </summary>
    public IReadOnlyList<Attachment> Attachments { get; }

    /// <summary>
    /// Gets the mode.
    /// </summary>
    public PromptMode Mode { get; }
}
