using System.Collections.Generic;

namespace Omp.Core;

/// <summary>
/// Represents a base response structure for user interactions, providing factory methods to create responses based on values, confirmations, cancellations, or sets of answers.
/// </summary>
public abstract class InteractionResponse
{
    /// <summary>
    /// Creates an interaction response containing the specified string value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The interaction response result.</returns>
    public static InteractionResponse FromValue(string value) => new ValueResponse { Value = value };

    /// <summary>
    /// Creates an interaction response indicating whether the action was confirmed.
    /// </summary>
    /// <param name="confirmed">The confirmed.</param>
    /// <returns>The interaction response result.</returns>
    public static InteractionResponse FromConfirmed(bool confirmed) => new ConfirmedResponse { Confirmed = confirmed };

    /// <summary>
    /// Creates an interaction response indicating that the operation was cancelled.
    /// </summary>
    /// <returns>The interaction response result.</returns>
    public static InteractionResponse Cancelled() => new CancelledResponse();

    /// <summary>
    /// Creates an interaction response based on the provided collection of question-and-answer pairs.
    /// </summary>
    /// <param name="answers">The collection of answers.</param>
    /// <returns>The interaction response result.</returns>
    public static InteractionResponse FromAnswers(IReadOnlyList<AskAnswer> answers) => new AnswersResponse { Answers = answers };
}
