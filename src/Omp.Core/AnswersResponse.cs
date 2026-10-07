using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class AnswersResponse : InteractionResponse { public IReadOnlyList<AskAnswer> Answers { get; set; } = Array.Empty<AskAnswer>(); }
