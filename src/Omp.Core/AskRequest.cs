using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class AskRequest : InteractionRequest
{
    public IReadOnlyList<AskQuestionView> Questions { get; set; } = Array.Empty<AskQuestionView>();
}
