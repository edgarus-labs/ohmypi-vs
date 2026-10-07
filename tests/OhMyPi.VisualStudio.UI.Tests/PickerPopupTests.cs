using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OhMyPi.VisualStudio.UI.Views;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>The searchable picker list and its popup: filtering, keyboard and mouse picking, group headers, dismissing.</summary>
    [Collection("wpf")]
    public class PickerPopupTests
    {
        private static IReadOnlyList<PickerItem> Source(string query)
        {
            var all = new[] { "alpha", "beta", "gamma" };
            var items = new List<PickerItem> { new PickerItem(null, new TextBlock { Text = "Group" }, "Group") };
            items.AddRange(all.Where(name => name.Contains(query)).Select(name => new PickerItem(name, new TextBlock { Text = name }, name)));
            return query == "none" ? new PickerItem[0] : items;
        }

        private static void Host(Window window, UIElement element)
        {
            window.Resources.MergedDictionaries.Add(new OmpChatControl().Resources);
            window.Content = element;
            Pump();
        }

        private static ListBoxItem Row(PickerList list, string name) => list.List.Items.Cast<ListBoxItem>().Single(row => System.Windows.Automation.AutomationProperties.GetName(row) == name);

        private static void Release(UIElement target) =>
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });

        [Fact]
        public void Search_filters_the_rows_and_keys_move_past_headers_and_pick()
        {
            RunStaWindow(window =>
            {
                var list = new PickerList("Things", searchable: true);
                var picked = new List<object>();
                list.Picked += picked.Add;
                Host(window, list);
                list.SetSource(Source);
                Pump();
                var search = Named<TextBox>(list, "Search Things");
                Assert.Equal("alpha", ((ListBoxItem)list.List.SelectedItem).Tag);

                Press(search, Key.Down);
                Press(search, Key.Down);
                Press(search, Key.Down);
                Assert.Equal("gamma", ((ListBoxItem)list.List.SelectedItem).Tag);
                Press(search, Key.Up);
                Press(search, Key.Up);
                Press(search, Key.Up);
                Assert.Equal("alpha", ((ListBoxItem)list.List.SelectedItem).Tag);
                Press(search, Key.Enter);
                Press(search, Key.A);
                Assert.Equal(new object[] { "alpha" }, picked);

                search.Text = "et";
                Pump();
                Assert.Equal(new[] { "Group", "beta" }, list.List.Items.Cast<ListBoxItem>().Select(System.Windows.Automation.AutomationProperties.GetName));
                search.Text = "none";
                Pump();
                Assert.True(HasText(list, "No matches."));
                Press(search, Key.Enter);
                Assert.Single(picked);
            });
        }

        [Fact]
        public void Clicking_a_row_picks_it_and_headers_or_empty_space_pick_nothing()
        {
            RunStaWindow(window =>
            {
                var list = new PickerList("Things", searchable: true);
                var picked = new List<object>();
                list.Picked += picked.Add;
                Host(window, list);
                list.SetSource(Source);
                Pump();
                Release((UIElement)Row(list, "beta").Content);
                Release((UIElement)Row(list, "Group").Content);
                Release(list.List);
                Assert.Equal(new object[] { "beta" }, picked);
            });
        }

        [Fact]
        public void Escape_dismisses_and_select_highlights_a_value()
        {
            RunStaWindow(window =>
            {
                var list = new PickerList("Things", searchable: true, searchName: "Find");
                var dismissed = 0;
                list.Dismissed += () => dismissed++;
                Host(window, list);
                list.SetSource(Source);
                Pump();
                list.Select(value => (string)value == "gamma");
                Assert.Equal("gamma", ((ListBoxItem)list.List.SelectedItem).Tag);
                list.Select(value => (string)value == "missing");
                Assert.Equal("gamma", ((ListBoxItem)list.List.SelectedItem).Tag);
                Press(Named<TextBox>(list, "Find"), Key.Escape);
                Assert.Equal(1, dismissed);

                list.Reset("Loading…");
                Pump();
                Assert.Empty(list.List.Items);
                Assert.True(HasText(list, "Loading…"));
                list.FocusFirst();
                Pump();
                Assert.True(Named<TextBox>(list, "Find").IsKeyboardFocusWithin);
            });
        }

        [Fact]
        public void A_list_without_search_takes_focus_itself_and_enter_in_it_picks()
        {
            RunStaWindow(window =>
            {
                var list = new PickerList("Levels", searchable: false);
                var picked = new List<object>();
                list.Picked += picked.Add;
                Host(window, list);
                list.FocusFirst();
                list.SetSource(Source);
                Pump();
                Assert.True(list.List.IsKeyboardFocusWithin);
                Press(list.List, Key.Enter);
                Assert.Equal(new object[] { "alpha" }, picked);
                list.List.SelectedItem = null;
                Press(list.List, Key.Enter);
                Assert.Single(picked);
            });
        }

        [Fact]
        public void The_popup_opens_with_a_status_and_closes_when_a_value_is_picked()
        {
            RunStaWindow(window =>
            {
                var anchor = new Button { Content = "Effort" };
                Host(window, anchor);
                var popup = new PickerPopup(anchor, "Effort", searchable: false, width: 200);
                var picked = new List<object>();
                popup.Picked += picked.Add;
                popup.Open("Loading levels…");
                Pump();
                Assert.True(popup.Popup.IsOpen);
                Assert.True(HasText(popup.Content, "Loading levels…"));
                popup.SetStatus(null);
                popup.SetSource(Source);
                popup.Select(value => (string)value == "beta");
                Pump();
                Press(popup.Content.List, Key.Enter);
                Pump();
                Assert.Equal(new object[] { "beta" }, picked);
                Assert.False(popup.Popup.IsOpen);
                Assert.True(anchor.IsKeyboardFocused);
            });
        }
    }
}
