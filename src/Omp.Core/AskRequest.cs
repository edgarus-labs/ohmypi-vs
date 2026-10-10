using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents a request to initiate an interaction by posing a collection of specific questions.
/// </summary>
public sealed class AskRequest : InteractionRequest
{
    /// <summary>
    /// Gets or sets the collection of questions.
    /// </summary>
    public IReadOnlyList<AskQuestionView> Questions { get; set; } = Array.Empty<AskQuestionView>();
}
