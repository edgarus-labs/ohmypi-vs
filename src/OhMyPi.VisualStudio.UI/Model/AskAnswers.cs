using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

internal static class AskAnswers
{
    /// <summary>
    /// The <c>answers</c> payload per rpc.md: one entry per question in request order, exact option labels without
    /// duplicates; single-select takes at most one option and never both an option and free text; empty free text is omitted.
    /// </summary>
    public static IReadOnlyList<AskAnswer> Build(IReadOnlyList<AskQuestionView> questions, IReadOnlyList<AskQuestionState> states) => questions.Select((question, index) =>
                                                                                                                                           {
                                                                                                                                               var state = index < states.Count ? states[index] : new AskQuestionState(Array.Empty<string>(), "");
                                                                                                                                               var chosen = new HashSet<string>(state.Selected, StringComparer.Ordinal);
                                                                                                                                               var selectedOptions = question.Options.Select(option => option.Label).Where(chosen.Contains).Distinct(StringComparer.Ordinal).ToList();
                                                                                                                                               var customInput = state.Custom.Trim();
                                                                                                                                               if (!question.Multi)
                                                                                                                                               {
                                                                                                                                                   if (customInput.Length > 0)
                                                                                                                                                   {
                                                                                                                                                       return new AskAnswer { Id = question.Id, SelectedOptions = Array.Empty<string>(), CustomInput = customInput };
                                                                                                                                                   }

                                                                                                                                                   var first = state.Selected.FirstOrDefault(label => selectedOptions.Contains(label));

                                                                                                                                                   return new AskAnswer { Id = question.Id, SelectedOptions = first is null ? [] : new[] { first } };
                                                                                                                                               }

                                                                                                                                               return new AskAnswer { Id = question.Id, SelectedOptions = selectedOptions, CustomInput = customInput.Length > 0 ? customInput : null };
                                                                                                                                           }).ToList();
}
