using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Represents a multimodal prompt consisting of a text message and a collection of associated images.
/// </summary>
internal sealed class ComposedPrompt
{
    /// <summary>
    /// Initializes a new instance of the ComposedPrompt class with the specified message and collection of images.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="images">The collection of images.</param>
    public ComposedPrompt(string message, IReadOnlyList<PromptImage> images)
    {
        Message = message;
        Images = images;
    }

    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the collection of images.
    /// </summary>
    public IReadOnlyList<PromptImage> Images { get; }
}
