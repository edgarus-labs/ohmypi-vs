using System;
using System.Collections.Generic;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>Completion and detection of OMP slash commands typed in the composer.</summary>
    internal static class SlashCommands
    {
        /// <summary>OMP runs a prompt as a slash command only when its text starts with "/".</summary>
        public static bool IsCommand(string text) => text.TrimStart().StartsWith("/", StringComparison.Ordinal);

        /// <summary>The command word typed so far (without "/"), or null once arguments or a new line follow.</summary>
        public static string? Query(string text)
        {
            if (!text.StartsWith("/", StringComparison.Ordinal)) return null;
            var word = text.Substring(1);
            return word.Any(char.IsWhiteSpace) ? null : word;
        }

        /// <summary>
        /// Commands whose name or alias is exactly <paramref name="query"/>, then those starting with it, then those
        /// containing it; OMP's order within each group. Enter accepts the first, so an exactly typed command wins.
        /// </summary>
        public static IReadOnlyList<SlashCommandView> Match(IReadOnlyList<SlashCommandView> commands, string query)
        {
            if (query.Length == 0) return commands;
            bool Exact(string value) => string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
            bool Prefix(string value) => value.StartsWith(query, StringComparison.OrdinalIgnoreCase);
            bool Contains(string value) => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            int Rank(SlashCommandView c) =>
                Exact(c.Name) || c.Aliases.Any(Exact) ? 0 : Prefix(c.Name) || c.Aliases.Any(Prefix) ? 1 : Contains(c.Name) || c.Aliases.Any(Contains) ? 2 : 3;
            return commands.Select(c => (Command: c, Rank: Rank(c))).Where(m => m.Rank < 3).OrderBy(m => m.Rank).Select(m => m.Command).ToList();
        }
    }
}
