using System;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Splits search text into words that must all match.</summary>
internal static class PickerSearch
{
    /// <summary>
    /// Splits the specified query string into an array of individual terms by removing whitespace and empty entries.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>A collection of string items.</returns>
    internal static string[] Terms(string query) => query.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    internal static bool Matches(string[] terms, params string?[] fields) =>
        terms.All(term => fields.Any(field => field != null && field.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0));
}
