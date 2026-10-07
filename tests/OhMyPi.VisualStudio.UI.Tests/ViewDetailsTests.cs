using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Views;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>Slash completion, renaming a session, long notices, messages and item kinds.</summary>
    [Collection("wpf")]
    public class ViewDetailsTests
    {
        private sealed class UnknownItem : TranscriptItem
        {
        }

        private static IEnumerable<DependencyObject> Everywhere() =>
            PresentationSource.CurrentSources.OfType<PresentationSource>().Select(source => source.RootVisual).Where(root => root != null).SelectMany(root => Descendants(root!));

        private static ListBox? Completions() =>
            Everywhere().OfType<ListBox>().FirstOrDefault(list => list.IsVisible && System.Windows.Automation.AutomationProperties.GetName(list) == "Slash commands");

        [Fact]
        public void Slash_completion_moves_accepts_by_key_or_click_and_closes()
        {
            var commands = new[] { new SlashCommandView { Name = "compact", Hint = "[focus]", Description = "Compact the context" }, new SlashCommandView { Name = "copy" }, new SlashCommandView { Name = "context" } };
            RunStaWindow(window =>
            {
                var composer = new Composer((_, __) => { }, (_, __) => { }) { Commands = commands };
                window.Resources.MergedDictionaries.Add(new OmpChatControl().Resources);
                window.Content = composer;
                Pump();
                composer.Input.Focus();
                composer.Input.Text = "/co";
                Pump();
                Assert.NotNull(Completions());
                Assert.True(composer.HandleKey(Key.Down, Key.None, ModifierKeys.None));
                Assert.True(composer.HandleKey(Key.Up, Key.None, ModifierKeys.None));
                Assert.True(composer.HandleKey(Key.Up, Key.None, ModifierKeys.None));
                Assert.False(composer.HandleKey(Key.A, Key.None, ModifierKeys.None));
                Assert.True(composer.HandleKey(Key.Tab, Key.None, ModifierKeys.None));
                Assert.Equal("/context ", composer.Input.Text);

                composer.Input.Text = "/co";
                Pump();
                composer.Commands = commands.Take(2).ToArray();
                Pump();
                var list = Completions()!;
                var row = list.Items.Cast<ListBoxItem>().Last();
                ((UIElement)row.Content).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
                Pump();
                Assert.Equal("/copy ", composer.Input.Text);

                composer.Input.Text = "/co";
                Pump();
                Assert.True(composer.HandleKey(Key.Escape, Key.None, ModifierKeys.None));
                Pump();
                Assert.Null(Completions());
                Assert.False(composer.HandleKey(Key.Down, Key.None, ModifierKeys.None));

                composer.Input.Text = "/compact";
                Pump();
                Assert.Null(Completions());
                composer.Input.Text = "/co";
                Pump();
                Keyboard.ClearFocus();
                Pump();
                Assert.Null(Completions());
                composer.Commands = null!;
                Assert.Empty(composer.Commands);
            });
        }

        [Fact]
        public void Renaming_commits_on_enter_and_is_dropped_on_escape_empty_text_or_leaving()
        {
            var service = new FakeService();
            RunSta((window, control) =>
            {
                control.BeginRename();
                Pump();
                var box = Named<TextBox>(window, "Session name");
                box.Text = "Auth refactor";
                Press(box, Key.Enter);
                Pump();
                Assert.True(HasText(window, "Auth refactor"));

                control.BeginRename();
                Pump();
                box.Text = "Never";
                Press(box, Key.Escape);
                Pump();
                Assert.False(HasText(window, "Never"));

                control.BeginRename();
                Pump();
                box.Text = "   ";
                Press(box, Key.A);
                Press(box, Key.Enter);
                Pump();
                Assert.True(HasText(window, "Auth refactor"));

                control.BeginRename();
                Pump();
                box.Text = "Left behind";
                Named<TextBox>(window, "Prompt").Focus();
                Pump();
                Assert.False(box.IsVisible);
            }, service, new FakeHost());
        }

        [Fact]
        public void A_long_notice_expands_by_its_toggle_or_by_clicking_and_collapses_again()
        {
            var service = new FakeService { Transcript = new TranscriptItem[] { new NoticeItem { Id = "n1", Level = NoticeLevel.Warning, Text = "first line\nsecond line" } } };
            RunSta((window, control) =>
            {
                var toggle = Named<ToggleButton>(window, "Show the full message");
                toggle.IsChecked = true;
                Pump();
                Assert.Equal("Collapse message", System.Windows.Automation.AutomationProperties.GetName(toggle));
                toggle.IsChecked = false;
                Pump();
                var label = Descendants(window).OfType<TextBlock>().First(t => t.IsVisible && t.Text == "first line\nsecond line");
                label.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                Pump();
                Assert.True(toggle.IsChecked);
            }, service, new FakeHost());
        }

        [Fact]
        public void Messages_show_pasted_text_file_chips_thinking_errors_and_meta_and_unknown_items_are_flagged()
        {
            var user = new UserItem { Id = "u1", Text = "Look at this\n\n@src/a.cs\n\n<pasted name=\"log.txt\">\nline 1\nline 2\n</pasted>" };
            var answer = new AssistantItem { Id = "a1", Text = "Done", Thinking = "step one\nstep two", Model = "opus", Usage = new UsageView { Input = 1200, Output = 30, CostUsd = 0.01 }, StopReason = "length", ErrorMessage = "cut short" };
            var service = new FakeService { Transcript = new TranscriptItem[] { user, answer, new UnknownItem { Id = "x" }, new CommandOutputItem { Id = "c", Text = "$ ls\nsrc/a.cs" } } };
            var host = new FakeHost();
            RunSta((window, control) =>
            {
                Assert.True(HasText(window, "Unsupported item UnknownItem"));
                Assert.True(HasText(window, "Thinking · 2 lines"));
                Assert.True(HasText(window, "cut short"));
                Assert.True(HasText(window, "opus"));
                Assert.True(HasText(window, "length"));
                Named<ToggleButton>(window, "Thinking · 2 lines").IsChecked = true;
                Pump();
                Assert.True(HasText(window, "step two"));

                service.RaiseItem(new AssistantItem { Id = "a1", Text = "", Thinking = "more", Streaming = true });
                Pump(300);
                Assert.True(HasText(window, "Thinking…"));
            }, service, host);
        }
    }
}
