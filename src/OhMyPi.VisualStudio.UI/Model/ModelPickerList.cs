using System;
using System.Collections.Generic;
using System.Linq;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>One model of the catalog; <see cref="Variant"/> tells apart entries of one provider that share a display name.</summary>
    internal sealed class ModelEntry
    {
        public ModelEntry(ModelView model, string? variant)
        {
            Model = model;
            Variant = variant;
            Key = new ModelKey(model.Provider, model.Id);
        }

        public ModelView Model { get; }
        public ModelKey Key { get; }
        public string Name => Model.Name;
        public string Provider => Model.Provider;
        public string Id => Model.Id;
        public string? Variant { get; }
    }

    /// <summary>A provider node or a model of the picker tree, with the state the row shows.</summary>
    internal sealed class ModelPickerRow
    {
        private ModelPickerRow(string provider, int count, bool isExpanded, ModelEntry? entry, bool isActive, bool isFavorite)
        {
            Provider = provider;
            Count = count;
            IsExpanded = isExpanded;
            Entry = entry;
            IsActive = isActive;
            IsFavorite = isFavorite;
        }

        public static ModelPickerRow Group(string provider, int count, bool isExpanded) => new ModelPickerRow(provider, count, isExpanded, null, false, false);

        public static ModelPickerRow Model(ModelEntry entry, bool isActive, bool isFavorite) => new ModelPickerRow(entry.Provider, 0, false, entry, isActive, isFavorite);

        public bool IsGroup => Entry == null;
        public string Provider { get; }
        /// <summary>For a provider node: how many of its models the current search and filters leave.</summary>
        public int Count { get; }
        /// <summary>For a provider node: whether its models are listed under it.</summary>
        public bool IsExpanded { get; }
        public ModelEntry? Entry { get; }
        /// <summary>The model the conversation uses now.</summary>
        public bool IsActive { get; }
        public bool IsFavorite { get; }
    }

    /// <summary>What narrows the picker list: the search words and the three filters, which all apply together.</summary>
    internal sealed class ModelPickerFilter
    {
        public string Query { get; set; } = "";
        public string? Provider { get; set; }
        public bool FavoritesOnly { get; set; }
        public bool RecentOnly { get; set; }
    }

    /// <summary>The user's stars and picks and the model in use, which decide what the rows show and where they sit.</summary>
    internal sealed class ModelUsage
    {
        public ModelUsage(IReadOnlyCollection<ModelKey> favorites, IReadOnlyList<ModelKey> recents, ModelKey? active)
        {
            Favorites = favorites;
            Recents = recents;
            Active = active;
        }

        public IReadOnlyCollection<ModelKey> Favorites { get; }
        /// <summary>Most recent first.</summary>
        public IReadOnlyList<ModelKey> Recents { get; }
        public ModelKey? Active { get; }
    }

    internal sealed class ProviderOption
    {
        public ProviderOption(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }
        public int Count { get; }
    }

    /// <summary>The models OMP reports: unique per provider and id, grouped by provider, searchable and filterable.</summary>
    internal sealed class ModelCatalog
    {
        private static readonly char[] Separators = { '-', '_', '.', '/', ':', '@', ' ' };

        private readonly Dictionary<ModelKey, ModelEntry> _byKey = new Dictionary<ModelKey, ModelEntry>();

        public ModelCatalog(IEnumerable<ModelView> models)
        {
            var seen = new HashSet<ModelKey>();
            var unique = models.Where(model => seen.Add(new ModelKey(model.Provider, model.Id))).ToList();
            var variants = Variants(unique);
            var entries = unique
                .Select(model => new ModelEntry(model, variants.TryGetValue(new ModelKey(model.Provider, model.Id), out var variant) ? variant : null))
                .OrderBy(entry => entry.Name, NaturalComparer.Instance)
                .ThenBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Provider, StringComparer.Ordinal)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .ToList();
            foreach (var entry in entries) _byKey[entry.Key] = entry;
            Entries = entries;
        }

        /// <summary>Every model, sorted by name, then provider, then id.</summary>
        public IReadOnlyList<ModelEntry> Entries { get; }

        /// <summary>
        /// The tree to show: one node per provider with models left by the search and filters, sorted by provider name,
        /// each followed by its models when it is in <paramref name="expanded"/>. While a search or a filter is on, every
        /// node with a match is expanded and nodes without one are left out. Under the Recent filter models follow
        /// the order they were picked in; otherwise they are grouped by family, newest first (see <see cref="NewestFirst"/>).
        /// </summary>
        public IReadOnlyList<ModelPickerRow> Rows(ModelPickerFilter filter, ModelUsage usage, IReadOnlyCollection<string> expanded)
        {
            var favorites = new HashSet<ModelKey>(usage.Favorites);
            var recentRank = new Dictionary<ModelKey, int>();
            foreach (var key in usage.Recents)
            {
                if (_byKey.ContainsKey(key) && !recentRank.ContainsKey(key)) recentRank[key] = recentRank.Count;
            }
            var terms = PickerSearch.Terms(filter.Query);
            var narrowed = terms.Length > 0 || filter.Provider != null || filter.FavoritesOnly || filter.RecentOnly;
            var open = new HashSet<string>(expanded, StringComparer.Ordinal);

            var matching = Entries.Where(entry =>
                (filter.Provider == null || entry.Provider == filter.Provider)
                && (!filter.FavoritesOnly || favorites.Contains(entry.Key))
                && (!filter.RecentOnly || recentRank.ContainsKey(entry.Key))
                && terms.All(term => Contains(entry.Name, term) || Contains(entry.Id, term) || Contains(entry.Provider, term)));
            if (filter.RecentOnly) matching = matching.OrderBy(entry => recentRank[entry.Key]);

            var rows = new List<ModelPickerRow>();
            foreach (var group in matching.GroupBy(entry => entry.Provider)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                var models = filter.RecentOnly ? group.ToList() : NewestFirst(group);
                var isExpanded = narrowed || open.Contains(group.Key);
                rows.Add(ModelPickerRow.Group(group.Key, models.Count, isExpanded));
                if (isExpanded)
                    rows.AddRange(models.Select(entry => ModelPickerRow.Model(entry, entry.Key.Equals(usage.Active), favorites.Contains(entry.Key))));
            }
            return rows;
        }

        /// <summary>
        /// Models grouped by their family (vendor class and family, as OMP reports them), the family with the newest
        /// revision first; within a family the newest revision first, then by name. Models without a family come last,
        /// by name.
        /// </summary>
        private static List<ModelEntry> NewestFirst(IEnumerable<ModelEntry> models)
        {
            var list = models.ToList();
            var families = list.Where(entry => FamilyOf(entry) != null)
                .GroupBy(FamilyOf)
                .Select(family => (Entries: family.OrderByDescending(entry => Revision(entry)).ThenBy(entry => entry.Name, NaturalComparer.Instance).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToList(),
                    Newest: family.Max(entry => Revision(entry))!))
                .OrderByDescending(family => family.Newest)
                .ThenBy(family => FamilyOf(family.Entries[0]), StringComparer.OrdinalIgnoreCase)
                .SelectMany(family => family.Entries);
            var rest = list.Where(entry => FamilyOf(entry) == null).OrderBy(entry => entry.Name, NaturalComparer.Instance).ThenBy(entry => entry.Id, StringComparer.Ordinal);
            return families.Concat(rest).ToList();
        }

        private static string? FamilyOf(ModelEntry entry) =>
            string.IsNullOrEmpty(entry.Model.Family) ? null : (entry.Model.VendorClass ?? "") + "/" + entry.Model.Family;

        /// <summary>The model's revision, "0" when OMP gives none, so such models sort below versioned ones of their family.</summary>
        private static Version Revision(ModelEntry entry) =>
            Version.TryParse(entry.Model.Revision ?? "", out var version) ? version : new Version(0, 0);

        /// <summary>The providers with models in the catalog whose name contains every word of <paramref name="search"/>, sorted by name.</summary>
        public IReadOnlyList<ProviderOption> Providers(string search)
        {
            var terms = PickerSearch.Terms(search);
            return Entries
                .GroupBy(entry => entry.Provider)
                .Where(group => terms.All(term => Contains(group.Key, term)))
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ProviderOption(group.Key, group.Count()))
                .ToList();
        }

        private static bool Contains(string text, string term) => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// For models of one provider that share a display name: the part of the id that differs, cut at separators;
        /// the full id when that would leave an entry with nothing or when the parts would start alike, as with
        /// "gpt-5" and "gpt-5-codex", whose cut parts "5" and "5-codex" would not tell them apart at a glance.
        /// </summary>
        private static Dictionary<ModelKey, string> Variants(IEnumerable<ModelView> models)
        {
            var variants = new Dictionary<ModelKey, string>();
            foreach (var group in models.GroupBy(model => (model.Provider, Name: model.Name.ToUpperInvariant())).Where(group => group.Count() > 1))
            {
                var ids = group.Select(model => model.Id).ToList();
                var prefix = CommonPrefix(ids);
                var suffix = CommonSuffix(ids, prefix);
                var remainders = ids.Select(id => id.Substring(prefix, id.Length - prefix - suffix)).ToList();
                var useFull = remainders.Any(remainder => remainder.Length == 0)
                    || remainders.Select(remainder => remainder.Split(Separators)[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() < remainders.Count;
                for (var i = 0; i < ids.Count; i++) variants[new ModelKey(group.Key.Provider, ids[i])] = useFull ? ids[i] : remainders[i];
            }
            return variants;
        }

        /// <summary>Length of the longest prefix all <paramref name="ids"/> share that ends right after a separator.</summary>
        private static int CommonPrefix(IReadOnlyList<string> ids)
        {
            var first = ids[0];
            var length = 0;
            while (length < first.Length && ids.All(id => length < id.Length && id[length] == first[length])) length++;
            while (length > 0 && Array.IndexOf(Separators, first[length - 1]) < 0) length--;
            return length;
        }

        /// <summary>Length of the longest suffix all <paramref name="ids"/> share, past <paramref name="prefix"/>, that starts at a separator.</summary>
        private static int CommonSuffix(IReadOnlyList<string> ids, int prefix)
        {
            var first = ids[0];
            var length = 0;
            while (length < first.Length - prefix && ids.All(id => length < id.Length - prefix && id[id.Length - 1 - length] == first[first.Length - 1 - length])) length++;
            while (length > 0 && Array.IndexOf(Separators, first[first.Length - length]) < 0) length--;
            return length;
        }

        /// <summary>Orders text ignoring case, with runs of digits compared by value so "GPT-5" sorts before "GPT-10".</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();

            public int Compare(string? x, string? y)
            {
                x ??= "";
                y ??= "";
                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        var startX = i;
                        var startY = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;
                        var numberX = x.Substring(startX, i - startX).TrimStart('0');
                        var numberY = y.Substring(startY, j - startY).TrimStart('0');
                        var byLength = numberX.Length.CompareTo(numberY.Length);
                        if (byLength != 0) return byLength;
                        var byValue = string.CompareOrdinal(numberX, numberY);
                        if (byValue != 0) return byValue;
                        continue;
                    }
                    var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (byChar != 0) return byChar;
                    i++;
                    j++;
                }
                return (x.Length - i).CompareTo(y.Length - j);
            }
        }
    }
}
