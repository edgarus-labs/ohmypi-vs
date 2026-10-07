using System.Collections.Generic;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    internal sealed class EffortOption
    {
        public EffortOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }
        public string Label { get; }
    }

    internal sealed class EffortChoices
    {
        public EffortChoices(string? value, IReadOnlyList<EffortOption> options)
        {
            Value = value;
            Options = options;
        }

        /// <summary>The user's selector (OMP <c>configured</c>), else the effective level.</summary>
        public string? Value { get; }
        public IReadOnlyList<EffortOption> Options { get; }
    }

    /// <summary>
    /// Reasoning-effort selector: the value is the user's
    /// selector and <c>auto</c> names the effort it resolved to once OMP classified the turn.
    /// </summary>
    internal static class Effort
    {
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
            if (choices.Options.Count == 0) return "";
            return choices.Options.FirstOrDefault(option => option.Value == choices.Value)?.Label ?? choices.Value ?? "";
        }

        public static string Tooltip(SessionView session)
        {
            var value = Choices(session).Value;
            if (value != "auto") return "Reasoning effort";
            return $"Reasoning effort: auto (OMP picks it per turn{(string.IsNullOrEmpty(session.ThinkingLevel) ? "" : $", now {session.ThinkingLevel}")})";
        }
    }
}
