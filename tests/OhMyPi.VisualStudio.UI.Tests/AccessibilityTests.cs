using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Views;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>Keyboard focus, screen-reader announcements, selectable text and zoom-aware layout of the chat.</summary>
    [Collection("wpf")]
    public class AccessibilityTests
    {
        private static string ProseText(RichTextBox box) => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;

        [Fact]
        public void Answering_the_last_card_from_the_keyboard_returns_focus_to_the_prompt()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                window.Activate();
                Task.Run(() => service.RaiseInteraction(new ConfirmRequest { Id = "c1", Title = "Run it?" })).Wait();
                Pump(200);
                var deny = Named<Button>(window, "Deny");
                Keyboard.Focus(deny);
                Assert.True(deny.IsKeyboardFocused);
                Click(deny);
                Pump();
                Assert.True(Named<TextBox>(window, "Prompt").IsKeyboardFocused);
            }, service, new FakeHost());
        }

        [Fact]
        public void Answering_a_card_never_moves_focus_onto_the_next_cards_approve_button()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                window.Activate();
                Task.Run(() =>
                {
                    service.RaiseInteraction(new ConfirmRequest { Id = "c1", Title = "First?" });
                    service.RaiseInteraction(new ConfirmRequest { Id = "c2", Title = "Second?" });
                }).Wait();
                Pump(200);
                var approve = AllNamed<Button>(window, "Approve").First();
                Keyboard.Focus(approve);
                Click(approve);
                Pump();
                var focused = Keyboard.FocusedElement as FrameworkElement;
                Assert.NotNull(focused);
                Assert.False(focused is ButtonBase, $"focus moved to the button {AutomationProperties.GetName(focused!)}");
                Assert.Equal("Second?", AutomationProperties.GetName(focused!));
                Assert.Equal("c1", Assert.Single(service.Responses).Id);
            }, service, new FakeHost());
        }

        [Fact]
        public void New_cards_the_banner_and_the_state_word_announce_themselves_and_the_ticking_pill_does_not()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                Task.Run(() =>
                {
                    service.RaiseInteraction(new ConfirmRequest { Id = "c1", Title = "Run it?" });
                    service.RaiseConnection(new ConnectionStatus { State = ConnectionState.Failed, Detail = "exit 1" });
                }).Wait();
                Pump(200);
                var title = Descendants(control.InteractionsScroller).OfType<TextBlock>().First(t => t.IsVisible && t.Text == "Run it?");
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(title));
                var banner = Descendants(control.BannerHost).OfType<TextBlock>().First(t => t.IsVisible && t.Text == "OMP stopped: exit 1");
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(banner));
                var word = Descendants(control.HeaderHost).OfType<TextBlock>().First(t => t.IsVisible && t.Text == "offline");
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(word));
                Assert.DoesNotContain(Descendants(window).OfType<Border>(), b => AutomationProperties.GetLiveSetting(b) != AutomationLiveSetting.Off);
            }, service, new FakeHost());
        }

        [Fact]
        public void The_conversation_can_be_focused_and_scrolled_from_the_keyboard()
        {
            var service = new FakeService { Transcript = Enumerable.Range(0, 80).Select(i => (TranscriptItem)new UserItem { Id = "u" + i, Text = "message " + i }).ToArray() };
            RunSta((window, control) =>
            {
                window.Activate();
                Pump(200);
                var scroller = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "PART_Scroller");
                Assert.Equal("Conversation", AutomationProperties.GetName(scroller));
                Assert.True(scroller.Focus(), "the conversation did not take focus");
                Assert.NotNull(scroller.FocusVisualStyle);
                scroller.ScrollToEnd();
                Pump();
                var bottom = scroller.VerticalOffset;
                Assert.True(bottom > 0, "the conversation is not scrolled");
                Press(scroller, Key.PageUp);
                Pump(300);
                Assert.True(scroller.VerticalOffset < bottom, $"PageUp left the offset at {scroller.VerticalOffset} (bottom {bottom})");
            }, service, new FakeHost());
        }

        [Fact]
        public void Abandoning_a_rename_by_moving_focus_leaves_focus_where_the_user_put_it()
        {
            RunSta((window, control) =>
            {
                window.Activate();
                control.BeginRename();
                Pump();
                Assert.True(Named<TextBox>(window, "Session name").IsKeyboardFocused);
                var input = Named<TextBox>(window, "Prompt");
                Keyboard.Focus(input);
                Pump();
                Assert.True(input.IsKeyboardFocused);
                Assert.False(Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Session name").IsVisible);
            }, new FakeService(), new FakeHost());
        }

        [Fact]
        public void Session_history_takes_the_covered_sections_and_conversation_out_of_tab_navigation()
        {
            var service = new FakeService { Sessions = new[] { new SessionSummary { Path = "C:\\s\\a.jsonl", Title = "Debug VPN", Modified = 1, Size = 10 } } };
            RunSta((window, control) =>
            {
                var transcript = Named<FrameworkElement>(window, "Conversation");
                var sections = Named<ToggleButton>(window, "Agents");
                control.ShowSessionPicker();
                Pump(300);
                Assert.False(transcript.IsEnabled);
                Assert.False(sections.IsEnabled);
                Press(Named<TextBox>(window, "Search sessions"), Key.Escape);
                Pump();
                Assert.True(transcript.IsEnabled);
                Assert.True(sections.IsEnabled);
            }, service, new FakeHost());
        }

        [Fact]
        public void A_long_notice_expands_from_the_keyboard_into_selectable_text_and_stays_expanded()
        {
            var text = "Build failed\nerror CS0103: The name 'x' does not exist in the current context";
            var service = new FakeService { Transcript = new TranscriptItem[] { new NoticeItem { Id = "n1", Level = NoticeLevel.Error, Text = text } } };
            RunSta((window, control) =>
            {
                var toggle = Named<ToggleButton>(window, "Show the full message");
                Assert.True(toggle.Focusable);
                toggle.IsChecked = true;
                Pump();
                Assert.Contains(Descendants(window).OfType<RichTextBox>(), r => r.IsVisible && ProseText(r).Contains("error CS0103"));
                Task.Run(() => service.RaiseItem(new NoticeItem { Id = "n1", Level = NoticeLevel.Error, Text = text })).Wait();
                Pump(200);
                Assert.True(Named<ToggleButton>(window, "Collapse message").IsChecked);
            }, service, new FakeHost());
        }

        [Fact]
        public void Messages_and_thinking_render_as_selectable_text()
        {
            var service = new FakeService
            {
                Transcript = new TranscriptItem[]
                {
                    new UserItem { Id = "u1", Text = "Fix the login bug" },
                    new AssistantItem { Id = "a1", Text = "Done with **bold**.\n\n- first item\n\n# Title", Thinking = "plan it" },
                },
            };
            RunSta((window, control) =>
            {
                Named<ToggleButton>(window, "Thinking · 1 line").IsChecked = true;
                Pump();
                var prose = Descendants(window).OfType<RichTextBox>().Where(r => r.IsVisible).Select(ProseText).ToList();
                Assert.Contains(prose, t => t.Contains("Fix the login bug"));
                Assert.Contains(prose, t => t.Contains("Done with bold."));
                Assert.Contains(prose, t => t.Contains("first item"));
                Assert.Contains(prose, t => t.Contains("Title"));
                Assert.Contains(prose, t => t.Contains("plan it"));
            }, service, new FakeHost());
        }

    }
}
