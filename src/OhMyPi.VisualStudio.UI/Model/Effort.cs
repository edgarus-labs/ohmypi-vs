using Omp.Core;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>
/// Reasoning-effort selector: the value is the user's
/// selector and <c>auto</c> names the effort it resolved to once OMP classified the turn.
/// </summary>
internal static class Effort
{
    /// <summary>
    /// Generates a collection of effort options and the currently selected thinking level based on the provided session view.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The effort choices result.</returns>
    public static EffortChoices Choices(SessionView session)
    {
        var value = session.ThinkingSelector ?? session.ThinkingLevel;
        var options = session.AvailableThinkingLevels.Select(level => new EffortOption(
            level,
            level == "auto" && value == "auto" && !string.IsNullOrEmpty(session.ThinkingResolved) ? $"auto · {session.ThinkingResolved}" : level)).ToList();

        return new EffortChoices(value, options);
    }

    /// <summary>Text of the effort button, e.g. <c>auto · low</c>; empty when the model has no levels.</summary>
    public static string Label(SessionView session)
    {
        var choices = Choices(session);
        if (choices.Options.Count == 0)
        {
            return "";
        }

        return choices.Options.FirstOrDefault(option => option.Value == choices.Value)?.Label ?? choices.Value ?? "";
    }

    /// <summary>
    /// Generates a descriptive tooltip string indicating the reasoning effort configuration and current thinking level for the specified session view.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>The string result.</returns>
    public static string Tooltip(SessionView session)
    {
        var value = Choices(session).Value;
        if (value != "auto")
        {
            return "Reasoning effort";
        }

        return $"Reasoning effort: auto (OMP picks it per turn{(string.IsNullOrEmpty(session.ThinkingLevel) ? "" : $", now {session.ThinkingLevel}")})";
    }
}
