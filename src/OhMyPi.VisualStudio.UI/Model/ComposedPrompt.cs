using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model;

internal sealed class ComposedPrompt
{
    public ComposedPrompt(string message, IReadOnlyList<PromptImage> images)
    {
        Message = message;
        Images = images;
    }

    public string Message { get; }

    public IReadOnlyList<PromptImage> Images { get; }
}
