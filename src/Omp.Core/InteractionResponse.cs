using System.Collections.Generic;

namespace Omp.Core;

public abstract class InteractionResponse
{
    public static InteractionResponse FromValue(string value) => new ValueResponse { Value = value };

    public static InteractionResponse FromConfirmed(bool confirmed) => new ConfirmedResponse { Confirmed = confirmed };

    public static InteractionResponse Cancelled() => new CancelledResponse();

    public static InteractionResponse FromAnswers(IReadOnlyList<AskAnswer> answers) => new AnswersResponse { Answers = answers };
}
