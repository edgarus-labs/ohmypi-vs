using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Collections.Generic;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class ModelPickerListTests
{
    private static ModelView M(string provider, string id, string name) => new ModelView { Provider = provider, Id = id, Name = name };

    private static ModelKey K(string provider, string id) => new ModelKey(provider, id);

    private static readonly ModelView[] Models =
    [
        M("openai", "gpt-5", "GPT-5"),
        M("anthropic", "opus", "Claude Opus"),
        M("anthropic", "haiku", "Claude Haiku"),
        M("anthropic", "sonnet", "Claude Sonnet"),
        M("openai", "gpt-10", "GPT-10"),
    ];

    private static readonly ModelKey[] NoKeys = [];

    private static string Show(ModelPickerRow row) =>
        row.IsGroup
            ? $"{(row.IsExpanded ? "v" : ">")}{row.Provider}({row.Count})"
            : $"  {row.Entry!.Name}{(row.Entry.Variant is not null ? "~" + row.Entry.Variant : "")}{(row.IsActive ? "*" : "")}{(row.IsFavorite ? "+" : "")}";

    private static string[] Tree(IEnumerable<ModelView> models, ModelPickerFilter? filter = null, IReadOnlyCollection<ModelKey>? favorites = null, IReadOnlyList<ModelKey>? recents = null, ModelKey? active = null, IReadOnlyCollection<string>? expanded = null)
    {
        var catalog = new ModelCatalog(models);
        var open = expanded ?? catalog.Providers("").Select(p => p.Name).ToArray();

        return catalog.Rows(filter ?? new ModelPickerFilter(), new ModelUsage(favorites ?? NoKeys, recents ?? NoKeys, active), open).Select(Show).ToArray();
    }

    /// <summary>The model rows only, every provider expanded.</summary>
    private static string[] Rows(IEnumerable<ModelView> models, ModelPickerFilter? filter = null, IReadOnlyCollection<ModelKey>? favorites = null, IReadOnlyList<ModelKey>? recents = null, ModelKey? active = null) =>
        Tree(models, filter, favorites, recents, active).Where(row => row.StartsWith("  ")).Select(row => row.Substring(2)).ToArray();

    private static ModelPickerFilter Search(string query) => new ModelPickerFilter { Query = query };

    [Fact]
    public void Every_provider_is_a_node_with_its_model_count_and_only_expanded_ones_list_their_models()
    {
        Assert.Equal(
            new[] { "vanthropic(3)", "  Claude Haiku", "  Claude Opus", "  Claude Sonnet", ">openai(2)" },
            Tree(Models, expanded: new[] { "anthropic" }));
        Assert.Equal(new[] { ">anthropic(3)", ">openai(2)" }, Tree(Models, expanded: new string[0]));
    }

    [Fact]
    public void Models_of_a_provider_sort_by_name_with_numbers_in_numeric_order() => Assert.Equal(new[] { ">anthropic(3)", "vopenai(2)", "  GPT-5", "  GPT-10" }, Tree(Models, expanded: new[] { "openai" }));

    [Fact]
    public void The_active_model_and_the_favorites_are_flagged_on_their_rows()
    {
        var rows = Rows(Models, favorites: new[] { K("openai", "gpt-5"), K("anthropic", "opus") }, active: K("anthropic", "haiku"));
        Assert.Equal(new[] { "Claude Haiku*", "Claude Opus+", "Claude Sonnet", "GPT-5+", "GPT-10" }, rows);
    }

    [Fact]
    public void The_active_identity_is_provider_plus_model_id_not_the_id_alone()
    {
        var models = new[] { M("a", "shared", "Shared"), M("b", "shared", "Shared") };
        Assert.Equal(new[] { "va(1)", "  Shared", "vb(1)", "  Shared*" }, Tree(models, active: K("b", "shared")));
    }

    [Fact]
    public void The_same_provider_and_id_listed_twice_is_one_entry()
    {
        var models = new[] { M("a", "x", "First"), M("a", "x", "Second"), M("b", "x", "Third") };
        Assert.Equal(new[] { "First", "Third" }, Rows(models));
    }

    [Fact]
    public void Equal_names_sort_by_provider_then_id()
    {
        var models = new[] { M("b", "z", "Same"), M("b", "a", "Same"), M("a", "q", "Same") };
        var entries = new ModelCatalog(models).Entries.Select(e => e.Key.ToString()).ToArray();
        Assert.Equal(new[] { "a/q", "b/a", "b/z" }, entries);
    }

    [Fact]
    public void Search_shows_only_providers_with_matches_expanded_whatever_was_collapsed()
    {
        var collapsed = new string[0];
        Assert.Equal(new[] { "vanthropic(3)", "  Claude Haiku", "  Claude Opus", "  Claude Sonnet" }, Tree(Models, Search("CLAUDE"), expanded: collapsed));
        Assert.Equal(new[] { "vopenai(1)", "  GPT-10" }, Tree(Models, Search("gpt-10"), expanded: collapsed));
        Assert.Equal(new[] { "vopenai(2)", "  GPT-5", "  GPT-10" }, Tree(Models, Search("OpenAI"), expanded: collapsed));
        Assert.Empty(Tree(Models, Search("nothing like it"), expanded: collapsed));
    }

    [Fact]
    public void Several_search_words_must_all_match_in_any_field()
    {
        Assert.Equal(new[] { "Claude Sonnet" }, Rows(Models, Search("anthropic  SONNET")));
        Assert.Equal(new[] { "Claude Opus" }, Rows(Models, Search("opus claude")));
        Assert.Empty(Rows(Models, Search("openai claude")));
    }

    [Fact]
    public void The_provider_filter_keeps_that_provider_expanded_and_combines_with_search()
    {
        var filter = new ModelPickerFilter { Provider = "anthropic" };
        Assert.Equal(new[] { "vanthropic(3)", "  Claude Haiku", "  Claude Opus", "  Claude Sonnet" }, Tree(Models, filter, expanded: new string[0]));
        filter.Query = "son";
        Assert.Equal(new[] { "Claude Sonnet" }, Rows(Models, filter));
        filter.Query = "gpt";
        Assert.Empty(Tree(Models, filter));
    }

    [Fact]
    public void The_favorites_filter_keeps_only_starred_models()
    {
        var favorites = new[] { K("openai", "gpt-10"), K("anthropic", "haiku") };
        Assert.Equal(new[] { "vanthropic(1)", "  Claude Haiku+", "vopenai(1)", "  GPT-10+" }, Tree(Models, new ModelPickerFilter { FavoritesOnly = true }, favorites, expanded: new string[0]));
        Assert.Equal(new[] { "GPT-10+" }, Rows(Models, new ModelPickerFilter { FavoritesOnly = true, Query = "gpt" }, favorites));
    }

    [Fact]
    public void The_recent_filter_keeps_only_picked_models_most_recent_first_within_each_provider()
    {
        var recents = new[] { K("openai", "gpt-10"), K("anthropic", "opus"), K("anthropic", "haiku") };
        Assert.Equal(
            new[] { "vanthropic(2)", "  Claude Opus", "  Claude Haiku", "vopenai(1)", "  GPT-10" },
            Tree(Models, new ModelPickerFilter { RecentOnly = true }, recents: recents, expanded: new string[0]));
    }

    [Fact]
    public void Filters_combine_with_each_other()
    {
        var favorites = new[] { K("anthropic", "haiku"), K("openai", "gpt-5") };
        var recents = new[] { K("openai", "gpt-5"), K("anthropic", "opus") };
        var filter = new ModelPickerFilter { FavoritesOnly = true, RecentOnly = true };
        Assert.Equal(new[] { "GPT-5+" }, Rows(Models, filter, favorites, recents));
        filter.Provider = "anthropic";
        Assert.Empty(Tree(Models, filter, favorites, recents));
    }

    [Fact]
    public void Entries_sharing_a_name_within_a_provider_get_a_short_variant_and_others_do_not()
    {
        var models = new[]
        {
            M("anthropic", "claude-sonnet-4-20250514", "Claude Sonnet 4"),
            M("anthropic", "claude-sonnet-4-latest", "Claude Sonnet 4"),
            M("cursor", "claude-sonnet-4", "Claude Sonnet 4"),
            M("anthropic", "opus", "Claude Opus"),
        };
        Assert.Equal(
            new[] { "Claude Opus", "Claude Sonnet 4~20250514", "Claude Sonnet 4~latest", "Claude Sonnet 4" },
            Rows(models));
    }

    [Fact]
    public void The_variant_drops_the_prefix_and_suffix_all_duplicates_share()
    {
        var models = new[] { M("p", "m-1-fast", "M"), M("p", "m-2-fast", "M"), M("p", "m-3-fast", "M") };
        Assert.Equal(new[] { "M~1", "M~2", "M~3" }, Rows(models));
    }

    [Fact]
    public void The_variant_is_the_full_id_when_the_differing_parts_would_start_alike()
    {
        var models = new[] { M("p", "gpt-5", "GPT-5"), M("p", "gpt-5-codex", "GPT-5") };
        Assert.Equal(new[] { "GPT-5~gpt-5", "GPT-5~gpt-5-codex" }, Rows(models));

        var opus = new[] { M("anthropic", "claude-opus-4-5", "Claude Opus 4.5"), M("anthropic", "claude-opus-4-5-20251101", "Claude Opus 4.5") };
        Assert.Equal(new[] { "Claude Opus 4.5~claude-opus-4-5", "Claude Opus 4.5~claude-opus-4-5-20251101" }, Rows(opus));
    }

    [Fact]
    public void The_variant_is_the_full_id_when_one_id_would_leave_nothing()
    {
        var models = new[] { M("p", "m-", "M"), M("p", "m-x", "M") };
        Assert.Equal(new[] { "M~m-", "M~m-x" }, Rows(models));
    }

    [Fact]
    public void Variants_are_found_whatever_the_case_of_the_name_and_are_independent_of_the_filter()
    {
        var models = new[] { M("p", "a-1", "Name"), M("p", "a-2", "NAME") };
        Assert.Equal(new[] { "Name~1" }, Rows(models, Search("a-1")));
    }

    [Fact]
    public void Providers_are_listed_sorted_with_their_model_counts_and_searchable()
    {
        var catalog = new ModelCatalog(Models);
        Assert.Equal(new[] { "anthropic:3", "openai:2" }, catalog.Providers("").Select(p => $"{p.Name}:{p.Count}").ToArray());
        Assert.Equal(new[] { "openai:2" }, catalog.Providers("OPEN").Select(p => $"{p.Name}:{p.Count}").ToArray());
        Assert.Empty(catalog.Providers("zzz"));
    }

    [Fact]
    public void Hundreds_of_models_across_many_providers_stay_ordered_and_unique()
    {
        var models = Enumerable.Range(0, 600).Select(i => M("provider-" + (i % 40), "model-" + i, "Model " + (i % 150))).ToArray();
        var catalog = new ModelCatalog(models);
        var rows = catalog.Rows(new ModelPickerFilter(), new ModelUsage(NoKeys, NoKeys, null), catalog.Providers("").Select(p => p.Name).ToArray());
        Assert.Equal(640, rows.Count);
        Assert.Equal(40, rows.Count(r => r.IsGroup));
        Assert.Equal(600, rows.Where(r => !r.IsGroup).Select(r => r.Entry!.Key).Distinct().Count());
        Assert.Equal(40, catalog.Providers("").Count);
    }

    [Fact]
    public void Details_show_each_field_the_catalog_provides()
    {
        var details = ModelDetails.From(new ModelView
        {
            Provider = "anthropic",
            Id = "claude-sonnet-4-20250514",
            Name = "Claude Sonnet 4",
            ContextWindow = 200_000,
            MaxTokens = 64_000,
            Input = new[] { "text", "image" },
            ThinkingEfforts = new[] { "low", "medium", "high" },
            Reasoning = true,
            Cost = new ModelCostView { Input = 3, Output = 15 },
        });
        Assert.Equal(
            new[] { "Claude Sonnet 4", "anthropic", "claude-sonnet-4-20250514", "200k tokens", "64k tokens", "text, image", "low, medium, high", "$3 / 1M tokens", "$15 / 1M tokens" },
            new[] { details.Name, details.Provider, details.Id, details.Context, details.MaxOutput, details.InputTypes, details.Reasoning, details.InputPrice, details.OutputPrice });
    }

    [Fact]
    public void Missing_catalog_data_reads_not_provided_and_a_missing_price_is_not_a_zero_price()
    {
        var none = ModelDetails.From(M("p", "m", "M"));
        Assert.All(
            new[] { none.Context, none.MaxOutput, none.InputTypes, none.Reasoning, none.InputPrice, none.OutputPrice },
            value => Assert.Equal(ModelDetails.NotProvided, value));

        var half = ModelDetails.From(new ModelView { Provider = "p", Id = "m", Name = "M", Cost = new ModelCostView { Input = 0, Output = null }, ContextWindow = 0, MaxTokens = -1 });
        Assert.Equal("$0 / 1M tokens", half.InputPrice);
        Assert.Equal(ModelDetails.NotProvided, half.OutputPrice);
        Assert.Equal(ModelDetails.NotProvided, half.Context);
        Assert.Equal(ModelDetails.NotProvided, half.MaxOutput);
    }

    [Fact]
    public void Prices_keep_their_significant_decimals()
    {
        var details = ModelDetails.From(new ModelView { Provider = "p", Id = "m", Name = "M", Cost = new ModelCostView { Input = 0.25, Output = 0.0375 } });
        Assert.Equal("$0.25 / 1M tokens", details.InputPrice);
        Assert.Equal("$0.0375 / 1M tokens", details.OutputPrice);
    }

    [Fact]
    public void Reasoning_without_levels_says_supported_and_a_model_that_reports_nothing_says_not_provided()
    {
        Assert.Equal("Supported", ModelDetails.From(new ModelView { Provider = "p", Id = "m", Name = "M", Reasoning = true }).Reasoning);
        Assert.Equal(ModelDetails.NotProvided, ModelDetails.From(M("p", "m", "M")).Reasoning);
    }
}
