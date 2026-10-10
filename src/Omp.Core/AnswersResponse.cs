using System;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents the response containing a collection of answers associated with a specific interaction.
/// </summary>
/// <summary>
/// Gets or sets the collection of answers.
/// </summary>
public sealed class AnswersResponse : InteractionResponse { public IReadOnlyList<AskAnswer> Answers { get; set; } = Array.Empty<AskAnswer>(); }
