using OhMyPi.VisualStudio.UI.Model;
using OhMyPi.VisualStudio.UI.Views;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>What the model picker does with the user's searches, filters, clicks and keys.</summary>
[Collection("wpf")]
public sealed class ModelPickerViewTests
{
    private sealed class Preferences : IModelPreferences, IModelPickerFilters
    {
        private bool _favoritesOnly;
        private bool _recentOnly;

        public List<ModelKey> FavoriteKeys { get; } = new List<ModelKey>();

        public List<ModelKey> RecentKeys { get; } = new List<ModelKey>();

        public Exception? Failure { get; set; }

        public IReadOnlyList<ModelKey> Favorites => FavoriteKeys.ToArray();

        public IReadOnlyList<ModelKey> Recents => RecentKeys.ToArray();

        public bool FavoritesOnly
        {
            get => Failure is not null ? throw Failure : _favoritesOnly;
            set => _favoritesOnly = Failure is not null ? throw Failure : value;
        }

        public bool RecentOnly
        {
            get => Failure is not null ? throw Failure : _recentOnly;
            set => _recentOnly = Failure is not null ? throw Failure : value;
        }

        public void SetFavorite(ModelKey model, bool favorite)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            FavoriteKeys.Remove(model);
            if (favorite)
            {
                FavoriteKeys.Add(model);
            }
        }

        public void RecordPicked(ModelKey model)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            RecentKeys.Remove(model);
            RecentKeys.Insert(0, model);
        }
    }

    private sealed class PlainPreferences : IModelPreferences
    {
        public IReadOnlyList<ModelKey> Favorites => new ModelKey[0];

        public IReadOnlyList<ModelKey> Recents => new ModelKey[0];

        public void SetFavorite(ModelKey model, bool favorite) { }

        public void RecordPicked(ModelKey model) { }
    }

    private sealed class Picker
    {
        public Picker(Preferences preferences)
        {
            Preferences = preferences;
            View = new ModelPickerView(preferences, id => Copied.Add(id), (message, _) => Failures.Add(message));
            View.Picked += model => Picks.Add(model.Provider + "/" + model.Id);
            View.CloseRequested += () => Closed++;
        }

        public Preferences Preferences { get; }

        public ModelPickerView View { get; }

        public List<string> Picks { get; } = new List<string>();

        public List<string> Copied { get; } = new List<string>();

        public List<string> Failures { get; } = new List<string>();

        public int Closed { get; set; }

        public ListBox List => Named<ListBox>(View, "Models");

        public IReadOnlyList<ModelRowItem> Rows => List.Items.Cast<ModelRowItem>().ToList();

        public string[] Shown => [.. Rows.Select(row => row.IsGroup ? (row.IsExpanded ? "v " : "> ") + row.Provider : "  " + row.Id)];

        public string? Highlighted => (List.SelectedItem as ModelRowItem)?.Id;

        public ModelRowItem Row(string id) => Rows.Single(row => row.IsGroup ? row.Provider == id : row.Id == id);
    }

    private static readonly ModelView[] Models =
    [
        new ModelView { Provider = "anthropic", Id = "opus-5", Name = "Claude Opus 5", VendorClass = "anthropic", Family = "opus", Revision = "5.0.0" },
        new ModelView { Provider = "anthropic", Id = "opus-4", Name = "Claude Opus 4", VendorClass = "anthropic", Family = "opus", Revision = "4.0.0" },
        new ModelView { Provider = "openai", Id = "gpt-6", Name = "GPT-6", VendorClass = "openai", Family = "gpt", Revision = "6.0.0" },
        new ModelView { Provider = "openai", Id = "gpt-5", Name = "GPT-5", VendorClass = "openai", Family = "gpt", Revision = "5.0.0" },
    ];

    private static void Host(Window window, UIElement view)
    {
        window.Resources.MergedDictionaries.Add(new OmpChatControl().Resources);
        window.Content = view;
    }

    private static void Run(Action<Picker> body, Preferences? preferences = null, ModelKey? active = null) =>
        RunStaWindow(window =>
        {
            var picker = new Picker(preferences ?? new Preferences());
            Host(window, picker.View);
            picker.View.SetModels(Models, active);
            Pump();
            body(picker);
        });

    private static UIElement Target(Picker picker, ModelRowItem row)
    {
        picker.List.ScrollIntoView(row);
        Pump();
        var container = (ListBoxItem)picker.List.ItemContainerGenerator.ContainerFromItem(row);

        return Descendants(container).OfType<TextBlock>().First(text => text.IsVisible && !Descendants(container).OfType<ToggleButton>().Any(star => Descendants(star).Contains(text)));
    }

    private static void Mouse(UIElement target, RoutedEvent routedEvent) =>
        target.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent });

    private static void ClickOn(UIElement down, UIElement up)
    {
        Mouse(down, System.Windows.Input.Mouse.PreviewMouseDownEvent);
        Pump();
        Mouse(up, System.Windows.Input.Mouse.PreviewMouseUpEvent);
        Pump();
    }

    private static ToggleButton Star(Picker picker, ModelRowItem row)
    {
        picker.List.ScrollIntoView(row);
        Pump();

        return Descendants(picker.List.ItemContainerGenerator.ContainerFromItem(row)).OfType<ToggleButton>().Single();
    }

    [Fact]
    public void Only_the_provider_of_the_model_in_use_starts_expanded() => Run(picker => Assert.Equal(new[] { "v anthropic", "  opus-5", "  opus-4", "> openai" }, picker.Shown), active: new ModelKey("anthropic", "opus-4"));

    [Fact]
    public void Expanding_a_provider_collapses_the_others_and_collapsing_it_leaves_all_closed() => Run(picker =>
                                                                                                        {
                                                                                                            var openai = Target(picker, picker.Row("openai"));
                                                                                                            ClickOn(openai, openai);
                                                                                                            Assert.Equal(new[] { "> anthropic", "v openai", "  gpt-6", "  gpt-5" }, picker.Shown);

                                                                                                            var header = Target(picker, picker.Row("openai"));
                                                                                                            ClickOn(header, header);
                                                                                                            Assert.Equal(new[] { "> anthropic", "> openai" }, picker.Shown);
                                                                                                            Assert.Empty(picker.Picks);
                                                                                                        }, active: new ModelKey("anthropic", "opus-5"));

    [Fact]
    public void A_click_picks_a_model_only_when_pressed_and_released_on_the_same_row()
    {
        var preferences = new Preferences();
        Run(picker =>
        {
            ClickOn(Target(picker, picker.Row("opus-5")), Target(picker, picker.Row("opus-4")));
            Assert.Empty(picker.Picks);

            var row = Target(picker, picker.Row("opus-4"));
            ClickOn(row, row);
            Assert.Equal(new[] { "anthropic/opus-4" }, picker.Picks);
            Assert.Equal(new[] { new ModelKey("anthropic", "opus-4") }, preferences.RecentKeys);
        }, preferences, new ModelKey("anthropic", "opus-5"));
    }

    [Fact]
    public void The_star_of_a_row_toggles_the_favorite_without_picking_the_model()
    {
        var preferences = new Preferences();
        Run(picker =>
        {
            var star = Star(picker, picker.Row("opus-4"));
            ClickOn(star, star);
            star.IsChecked = true;
            Pump();
            Assert.Equal(new[] { new ModelKey("anthropic", "opus-4") }, preferences.FavoriteKeys);
            star.IsChecked = false;
            Pump();
            Assert.Empty(preferences.FavoriteKeys);
            Assert.Empty(picker.Picks);
        }, preferences, new ModelKey("anthropic", "opus-5"));
    }

    [Fact]
    public void Search_expands_every_provider_with_a_match_and_hides_the_rest() => Run(picker =>
                                                                                        {
                                                                                            Named<TextBox>(picker.View, "Search models").Text = "5";
                                                                                            Pump();
                                                                                            Assert.Equal(new[] { "v anthropic", "  opus-5", "v openai", "  gpt-5" }, picker.Shown);

                                                                                            var header = Target(picker, picker.Row("openai"));
                                                                                            ClickOn(header, header);
                                                                                            Assert.Equal(new[] { "v anthropic", "  opus-5", "v openai", "  gpt-5" }, picker.Shown);
                                                                                        });

    [Fact]
    public void Arrow_keys_move_over_models_only_and_enter_picks_the_highlighted_one() => Run(picker =>
                                                                                               {
                                                                                                   var search = Named<TextBox>(picker.View, "Search models");
                                                                                                   search.Text = "o";
                                                                                                   Pump();
                                                                                                   Press(search, Key.Down);
                                                                                                   Assert.Equal("opus-4", picker.Highlighted);
                                                                                                   Press(search, Key.PageDown);
                                                                                                   Assert.Equal("gpt-5", picker.Highlighted);
                                                                                                   Press(search, Key.Up);
                                                                                                   Assert.Equal("gpt-6", picker.Highlighted);
                                                                                                   Press(search, Key.PageUp);
                                                                                                   Assert.Equal("opus-5", picker.Highlighted);
                                                                                                   Press(search, Key.Enter);
                                                                                                   Assert.Equal(new[] { "anthropic/opus-5" }, picker.Picks);
                                                                                               });

    [Fact]
    public void Up_with_nothing_highlighted_starts_at_the_last_model_and_keys_do_nothing_without_models() => Run(picker =>
                                                                                                                  {
                                                                                                                      var search = Named<TextBox>(picker.View, "Search models");
                                                                                                                      search.Text = "gpt";
                                                                                                                      Pump();
                                                                                                                      picker.List.SelectedItem = null;
                                                                                                                      Press(search, Key.Up);
                                                                                                                      Assert.Equal("gpt-5", picker.Highlighted);

                                                                                                                      search.Text = "nothing matches";
                                                                                                                      Pump();
                                                                                                                      Press(search, Key.Down);
                                                                                                                      Press(search, Key.Enter);
                                                                                                                      Assert.Empty(picker.Picks);
                                                                                                                  });

    [Fact]
    public void Toggling_the_highlighted_favorite_saves_it()
    {
        var preferences = new Preferences();
        Run(picker =>
        {
            var search = Named<TextBox>(picker.View, "Search models");
            Press(search, Key.Up);
            Assert.True(picker.View.HandleKey(Key.D, ModifierKeys.Control));
            Assert.Equal(new[] { new ModelKey("anthropic", "opus-5") }, preferences.FavoriteKeys);
        }, preferences, new ModelKey("anthropic", "opus-4"));
    }

    [Fact]
    public void The_favorites_filter_shows_only_favorites_and_is_remembered_for_the_next_opening()
    {
        var preferences = new Preferences();
        preferences.FavoriteKeys.Add(new ModelKey("openai", "gpt-5"));
        Run(picker =>
        {
            var favorites = Named<ToggleButton>(picker.View, "Show favorites only");
            favorites.IsChecked = true;
            Click(favorites);
            Pump();
            Assert.Equal(new[] { "v openai", "  gpt-5" }, picker.Shown);
            Assert.True(preferences.FavoritesOnly);

            picker.View.SetModels(Models, null);
            Pump();
            Assert.Equal(new[] { "v openai", "  gpt-5" }, picker.Shown);
            Assert.True(Named<ToggleButton>(picker.View, "Show favorites only").IsChecked);
        }, preferences);
    }

    [Fact]
    public void The_recent_filter_lists_recently_picked_models_newest_first()
    {
        var preferences = new Preferences();
        preferences.RecentKeys.Add(new ModelKey("openai", "gpt-5"));
        preferences.RecentKeys.Add(new ModelKey("openai", "gpt-6"));
        Run(picker =>
        {
            var recent = Named<ToggleButton>(picker.View, "Show recently used only");
            recent.IsChecked = true;
            Click(recent);
            Pump();
            Assert.Equal(new[] { "v openai", "  gpt-5", "  gpt-6" }, picker.Shown);
            Assert.True(preferences.RecentOnly);

            recent.IsChecked = false;
            Click(recent);
            Pump();
            Assert.False(preferences.RecentOnly);
            Assert.Equal(new[] { "> anthropic", "> openai" }, picker.Shown);
        }, preferences);
    }

    [Fact]
    public void The_close_button_asks_to_close() => Run(picker =>
                                                         {
                                                             Click(Named<Button>(picker.View, "Close"));
                                                             Assert.Equal(1, picker.Closed);
                                                         });

    [Fact]
    public void A_status_replaces_the_list_until_models_arrive() => RunStaWindow(window =>
                                                                         {
                                                                             var picker = new Picker(new Preferences());
                                                                             Host(window, picker.View);
                                                                             picker.View.Reset("Loading models…");
                                                                             Pump();
                                                                             Assert.True(HasText(picker.View, "Loading models…"));
                                                                             Assert.Empty(picker.Rows);
                                                                             picker.View.SetStatus(null);
                                                                             Pump();
                                                                             Assert.False(HasText(picker.View, "Loading models…"));
                                                                         });

    [Fact]
    public void Failing_preferences_are_reported_and_the_pick_still_happens()
    {
        var preferences = new Preferences();
        Run(picker =>
        {
            preferences.Failure = new InvalidOperationException("store broken");
            var row = Target(picker, picker.Row("opus-4"));
            ClickOn(row, row);
            Star(picker, picker.Row("opus-4")).IsChecked = true;
            Click(Named<ToggleButton>(picker.View, "Show favorites only"));
            picker.View.SetModels(Models, null);
            Pump();

            Assert.Equal(new[] { "anthropic/opus-4" }, picker.Picks);
            Assert.Contains("Saving the recent models failed", picker.Failures);
            Assert.Contains("Saving the favorite models failed", picker.Failures);
            Assert.Contains("Saving the model filters failed", picker.Failures);
            Assert.Contains("Reading the model filters failed", picker.Failures);
        }, preferences, new ModelKey("anthropic", "opus-5"));
    }

    [Fact]
    public void Ctrl_c_copies_the_id_of_the_highlighted_model_unless_search_text_is_selected() => Run(picker =>
                                                                                                       {
                                                                                                           Assert.True(picker.View.HandleKey(Key.C, ModifierKeys.Control));
                                                                                                           Assert.Equal(new[] { "opus-5" }, picker.Copied);

                                                                                                           Assert.False(picker.View.HandleKey(Key.C, ModifierKeys.None));
                                                                                                           var search = Named<TextBox>(picker.View, "Search models");
                                                                                                           search.Text = "opus";
                                                                                                           search.SelectAll();
                                                                                                           Pump();
                                                                                                           Assert.False(picker.View.HandleKey(Key.C, ModifierKeys.Control));
                                                                                                           Assert.Single(picker.Copied);
                                                                                                           Assert.False(picker.View.HandleKey(Key.X, ModifierKeys.None));
                                                                                                       }, active: new ModelKey("anthropic", "opus-5"));

    [Fact]
    public void Ctrl_d_does_nothing_while_a_provider_header_is_highlighted()
    {
        var preferences = new Preferences();
        Run(picker =>
        {
            picker.List.SelectedItem = picker.Row("anthropic");
            Assert.True(picker.View.HandleKey(Key.D, ModifierKeys.Control));
            picker.List.SelectedItem = null;
            Assert.True(picker.View.HandleKey(Key.D, ModifierKeys.Control));
            Assert.True(picker.View.HandleKey(Key.Enter, ModifierKeys.None));
            Assert.Empty(preferences.FavoriteKeys);
            Assert.Empty(picker.Picks);
        }, preferences, new ModelKey("anthropic", "opus-5"));
    }

    [Fact]
    public void Scrolling_into_a_long_provider_pins_its_header_and_clicking_the_pin_collapses_it()
    {
        var many = Enumerable.Range(1, 40).Select(i => new ModelView { Provider = "anthropic", Id = $"m{i:00}", Name = $"Model {i:00}" }).ToArray();
        RunStaWindow(window =>
        {
            window.Height = 300;
            var picker = new Picker(new Preferences());
            Host(window, picker.View);
            picker.View.SetModels(many, new ModelKey("anthropic", "m01"));
            Pump(300);
            picker.List.ScrollIntoView(picker.Rows.Last());
            Pump(300);
            var pinned = Descendants(picker.View).OfType<ContentControl>().Single(c => !Descendants(picker.List).Contains(c) && c.IsVisible && c.Content is ModelRowItem row && row.IsGroup);
            Assert.Equal("anthropic", ((ModelRowItem)pinned.Content).Provider);

            pinned.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent });
            Pump(300);
            Assert.Equal(new[] { "> anthropic" }, picker.Shown);
            Assert.False(pinned.IsVisible);
        });
    }

    [Fact]
    public void Pointing_at_a_model_highlights_it() => Run(picker =>
                                                            {
                                                                var row = Target(picker, picker.Row("opus-4"));
                                                                row.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseMoveEvent });
                                                                Assert.Equal("opus-4", picker.Highlighted);
                                                                var header = Target(picker, picker.Row("anthropic"));
                                                                header.RaiseEvent(new MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseMoveEvent });
                                                                Assert.Equal("opus-4", picker.Highlighted);
                                                            }, active: new ModelKey("anthropic", "opus-5"));

    [Fact]
    public void A_press_outside_any_row_picks_nothing() => Run(picker =>
                                                                {
                                                                    ClickOn(picker.List, picker.List);
                                                                    Assert.Empty(picker.Picks);
                                                                }, active: new ModelKey("anthropic", "opus-5"));

    [Fact]
    public void The_list_says_when_omp_has_no_models_or_nothing_matches() => RunStaWindow(window =>
                                                                                  {
                                                                                      var picker = new Picker(new Preferences());
                                                                                      Host(window, picker.View);
                                                                                      Pump();
                                                                                      var search = Named<TextBox>(picker.View, "Search models");
                                                                                      search.Text = "typed before the models arrived";
                                                                                      Pump();
                                                                                      Assert.Empty(picker.Rows);

                                                                                      picker.View.SetModels(new ModelView[0], null);
                                                                                      Pump();
                                                                                      Assert.True(HasText(picker.View, "OMP reports no available models."));

                                                                                      picker.View.SetModels(Models, null);
                                                                                      Named<TextBox>(picker.View, "Search models").Text = "zzz";
                                                                                      Pump();
                                                                                      Assert.True(HasText(picker.View, "No models match."));
                                                                                      Named<TextBox>(picker.View, "Search models").Text = "";
                                                                                      Pump();
                                                                                      Assert.Equal(new[] { "> anthropic", "> openai" }, picker.Shown);
                                                                                  });

    [Fact]
    public void Filters_work_without_a_place_to_remember_them() => RunStaWindow(window =>
                                                                        {
                                                                            var view = new ModelPickerView(new PlainPreferences(), _ => { }, (message, _) => throw new Xunit.Sdk.XunitException(message));
                                                                            Host(window, view);
                                                                            view.SetModels(Models, null);
                                                                            Pump();
                                                                            var favorites = Named<ToggleButton>(view, "Show favorites only");
                                                                            favorites.IsChecked = true;
                                                                            Click(favorites);
                                                                            Pump();
                                                                            Assert.True(HasText(view, "No models match."));
                                                                        });

    [Fact]
    public void Clicking_the_search_box_frame_focuses_the_search_and_the_picker_takes_its_size() => Run(picker =>
                                                                                                         {
                                                                                                             picker.View.SetSize(new ModelPickerSize(400, 300));
                                                                                                             Pump();
                                                                                                             Assert.Equal(400, picker.View.ActualWidth, 0);
                                                                                                             Assert.Equal(300, picker.View.ActualHeight, 0);

                                                                                                             var search = Named<TextBox>(picker.View, "Search models");
                                                                                                             var frame = Descendants(picker.View).OfType<Border>().First(border => Descendants(border).Contains(search) && border.Child is DockPanel);
                                                                                                             frame.RaiseEvent(new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent });
                                                                                                             Pump();
                                                                                                             Assert.True(search.IsKeyboardFocusWithin);
                                                                                                             Keyboard.ClearFocus();
                                                                                                             picker.View.FocusSearch();
                                                                                                             Pump();
                                                                                                             Assert.True(search.IsKeyboardFocusWithin);
                                                                                                         });

    [Fact]
    public void A_row_shows_the_price_with_missing_halves_as_n_a_and_the_id_only_for_twins()
    {
        var models = new[]
        {
            new ModelView { Provider = "p", Id = "a-1", Name = "Twin", Cost = new ModelCostView { Input = 3 } },
            new ModelView { Provider = "p", Id = "a-2", Name = "Twin", Cost = new ModelCostView { Output = 15 } },
            new ModelView { Provider = "p", Id = "solo", Name = "Solo" },
        };
        RunStaWindow(window =>
        {
            var picker = new Picker(new Preferences());
            Host(window, picker.View);
            picker.View.SetModels(models, new ModelKey("p", "solo"));
            Pump();
            Assert.Equal("$3 / n/a", picker.Row("a-1").Price);
            Assert.Equal("n/a / $15", picker.Row("a-2").Price);
            Assert.Equal("", picker.Row("solo").Price);
            Assert.True(picker.Row("a-1").ShowId);
            Assert.False(picker.Row("solo").ShowId);
            Assert.Equal("Solo, p, solo, current model", picker.Row("solo").AutomationName);

            var row = picker.Row("a-1");
            var changes = 0;
            row.FavoriteChanged += _ => changes++;
            row.IsFavorite = true;
            row.IsFavorite = true;
            Assert.Equal(1, changes);
            Assert.Equal("Twin, p, a-1, favorite", row.AutomationName);
            Assert.Equal("p, 3 models, expanded", picker.Row("p").AutomationName);
            var group = picker.Row("p");
            Assert.Equal("", group.Name);
            Assert.Equal("", group.Id);
            Assert.False(group.ShowId);

            var detached = new ModelRowItem(new ModelCatalog(models).Rows(new ModelPickerFilter(), new ModelUsage(new ModelKey[0], new ModelKey[0], null), new[] { "p" }).Last());
            detached.IsFavorite = true;
            Assert.True(detached.IsFavorite);

            picker.List.SelectedItem = group;
            Named<TextBox>(picker.View, "Search models").Text = "twin";
            Pump();
            Assert.Equal("a-1", picker.Highlighted);
        });
    }
}
