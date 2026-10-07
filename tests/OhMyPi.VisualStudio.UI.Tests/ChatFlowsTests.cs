using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The chat control's flows: OMP presentations, session history, pickers, prompts that fail, zoom, copy and scrolling.</summary>
[Collection("wpf")]
public sealed class ChatFlowsTests
{
    private static void Send(Window window, string text)
    {
        var input = Named<TextBox>(window, "Prompt");
        input.Text = text;
        Press(input, Key.Enter);
        Pump(300);
    }

    /// <summary>Everything on screen: the window and every open popup.</summary>
    private static IEnumerable<DependencyObject> Everywhere() =>
        PresentationSource.CurrentSources.OfType<PresentationSource>().Select(source => source.RootVisual).Where(root => root != null).SelectMany(root => Descendants(root!));

    private static T Anywhere<T>(string name) where T : FrameworkElement =>
        Everywhere().OfType<T>().First(e => e.IsVisible && System.Windows.Automation.AutomationProperties.GetName(e) == name);

    private static bool ShownAnywhere(string fragment) =>
        PresentationSource.CurrentSources.OfType<PresentationSource>().Select(source => source.RootVisual).Where(root => root != null).Any(root => HasText(root!, fragment));

    private static SessionSummary Session(int i) => new SessionSummary { Path = $"C:\\s\\{i}.jsonl", Title = $"Session {i}", Modified = 1_000_000L * i, Size = 10, Cwd = i == 1 ? "C:\\repo" : null };

    [Fact]
    public void Omp_notices_status_texts_and_editor_text_reach_the_chat()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            service.RaisePresentation(new NotifyPresentation { Level = NoticeLevel.Warning, Message = "disk almost full" });
            service.RaisePresentation(new StatusPresentation { Key = "build", Text = "building…" });
            service.RaisePresentation(new EditorTextPresentation { Text = "draft from OMP" });
            Pump(300);
            Assert.True(HasText(window, "disk almost full"));
            Assert.True(HasText(window, "building…"));
            Assert.Equal("draft from OMP", Named<TextBox>(window, "Prompt").Text);

            service.RaisePresentation(new StatusPresentation { Key = "build", Text = null });
            Pump(300);
            Assert.False(HasText(window, "building…"));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_web_url_from_omp_opens_only_after_confirmation_and_other_urls_are_refused()
    {
        var service = new FakeService();
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            var launched = new List<string>();
            control.LaunchUrl = launched.Add;
            service.RaisePresentation(new OpenUrlPresentation { Url = "file:///C:/Windows/notepad.exe" });
            service.RaisePresentation(new OpenUrlPresentation { Url = "https://example.com/a b", Instructions = "Sign in" });
            Pump(300);
            Assert.True(HasText(window, "OMP asked to open a non-web URL; ignored: file:///C:/Windows/notepad.exe"));
            Assert.True(HasText(window, "Sign in\nhttps://example.com/a b"));
            Click(Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Content is string s && s == "Open"));
            Pump();
            Assert.Equal(new[] { "https://example.com/a%20b" }, launched);

            control.LaunchUrl = _ => throw new InvalidOperationException("no browser");
            service.RaisePresentation(new OpenUrlPresentation { Url = "https://example.com" });
            Pump(300);
            Click(Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Content is string s && s == "Open"));
            Pump();
            Assert.True(HasText(window, "Opening https://example.com failed: no browser"));
            Assert.Contains(host.Errors, error => error.Message == "Opening https://example.com failed");

            service.RaisePresentation(new OpenUrlPresentation { Url = "https://example.com/dismissed" });
            Pump(300);
            Click(Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Content is string s && s == "Dismiss"));
            Pump();
            Assert.False(HasText(window, "https://example.com/dismissed"));
        }, service, host);
    }

    [Fact]
    public void A_failure_while_applying_an_omp_event_becomes_a_logged_error()
    {
        var service = new FakeService();
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            service.RaiseInteraction(null!);
            Pump(300);
            Assert.Contains(host.Errors, error => error.Message == "Updating the chat failed");
        }, service, host);
    }

    [Fact]
    public void Recent_sessions_that_cannot_be_listed_are_reported()
    {
        var service = new FakeService { Session = new SessionView { SessionFile = "C:\\s\\current.jsonl" }, SessionsError = new InvalidOperationException("no access") };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Pump(300);
            Assert.True(HasText(window, "Listing recent sessions failed: no access"));
            Assert.Contains(host.Errors, error => error.Message == "Listing recent sessions failed");
        }, service, host);
    }

    [Fact]
    public void The_welcome_screen_resumes_a_session_and_view_all_opens_the_history()
    {
        var service = new FakeService { Sessions = Enumerable.Range(1, 8).Select(Session).ToArray(), Session = new SessionView { SessionFile = "C:\\s\\current.jsonl" } };
        RunSta((window, control) =>
        {
            control.RecentRefreshInterval = TimeSpan.FromMilliseconds(30);
            Pump(300);
            Click(Descendants(window).OfType<Button>().First(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == "Resume Session 1"));
            Pump();
            Assert.Equal(new[] { "C:\\s\\1.jsonl" }, service.Switches);

            Click(Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Content is TextBlock t && t.Text.StartsWith("View all", StringComparison.Ordinal)));
            Pump(300);
            Assert.True(Named<FrameworkElement>(window, "Session history").IsVisible);
        }, service, new FakeHost());
    }

    [Fact]
    public void Picking_a_session_from_the_history_switches_to_it_unless_it_is_the_current_one()
    {
        var service = new FakeService { Sessions = new[] { Session(1), Session(2) }, Session = new SessionView { SessionFile = "C:\\s\\2.jsonl" } };
        RunSta((window, control) =>
        {
            control.ShowSessionPicker();
            Pump(300);
            var search = Named<TextBox>(window, "Search sessions");
            search.Text = "Session 1";
            Pump();
            Press(search, Key.Enter);
            Pump(300);
            Assert.Equal(new[] { "C:\\s\\1.jsonl" }, service.Switches);
            Assert.DoesNotContain(AllNamed<FrameworkElement>(window, "Session history"), e => e is Border);

            control.ShowSessionPicker();
            Pump(300);
            search = Named<TextBox>(window, "Search sessions");
            search.Text = "Session 2";
            Pump();
            Press(search, Key.Enter);
            Pump(300);
            Assert.Single(service.Switches);

            control.ShowSessionPicker();
            Pump(300);
            Click(Named<Button>(window, "Close session history"));
            Pump();
            Assert.Empty(AllNamed<TextBox>(window, "Search sessions"));
        }, service, new FakeHost());
    }

    [Fact]
    public void The_history_says_when_there_are_no_sessions_or_they_cannot_be_listed()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            control.ShowSessionPicker();
            Pump(300);
            Assert.True(HasText(window, "No saved OMP sessions found."));
            service.SessionsError = new InvalidOperationException("locked");
            control.ShowSessionPicker();
            Pump(300);
            Assert.True(HasText(window, "Listing sessions failed: locked"));
        }, service, new FakeHost());
    }

    [Fact]
    public void The_effort_picker_offers_the_model_levels_and_sets_the_chosen_one()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            control.ShowThinkingPicker();
            Pump(300);
            Assert.True(HasText(window, "The current model has no thinking levels."));

            service.RaiseSession(new SessionView { AvailableThinkingLevels = new[] { "auto", "low", "high" }, ThinkingSelector = "auto", ThinkingResolved = "low" });
            Pump(300);
            control.ShowThinkingPicker();
            Pump(300);
            var list = Anywhere<ListBox>("Reasoning effort");
            list.SelectedIndex = 2;
            Press(list, Key.Enter);
            Pump(300);
            Assert.Equal(new[] { "high" }, service.ThinkingLevels);

            service.RaiseSession(new SessionView { AvailableThinkingLevels = new[] { "low", "high" } });
            Pump(300);
            control.ShowThinkingPicker();
            Pump(300);
            Press(Anywhere<ListBox>("Reasoning effort"), Key.Escape);
        }, service, new FakeHost());
    }

    [Fact]
    public void The_model_picker_reports_an_empty_catalog_and_listing_failures_and_sets_the_picked_model()
    {
        var service = new FakeService { Models = new ModelView[0] };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            control.ShowModelPicker();
            Pump(300);
            Assert.True(ShownAnywhere("OMP reports no available models. Configure providers in OMP."));
            Press(Anywhere<TextBox>("Search models"), Key.Escape);
            Pump();

            service.ListModelsError = new InvalidOperationException("offline");
            control.ShowModelPicker();
            Pump(300);
            Assert.True(ShownAnywhere("Listing models failed: offline"));
            Assert.True(ShownAnywhere("Select model failed: offline"));
            Press(Anywhere<TextBox>("Search models"), Key.Escape);
            Pump();

            service.ListModelsError = null;
            service.Models = new[] { new ModelView { Provider = "p", Id = "m1", Name = "M1" }, new ModelView { Provider = "p", Id = "m2", Name = "M2" } };
            service.RaiseSession(new SessionView { Model = new ModelView { Provider = "p", Id = "m1", Name = "M1" } });
            Pump(300);
            control.ShowModelPicker();
            Pump(300);
            control.ZoomTransform.ScaleX = 1.25;
            Pump();
            var search = Anywhere<TextBox>("Search models");
            Press(search, Key.Down);
            Press(search, Key.Enter);
            Pump(300);
            Assert.Equal(("p", "m2"), Assert.Single(service.ModelSelections));
        }, service, host);
    }

    [Fact]
    public void A_prompt_omp_cannot_take_restores_the_draft_and_reports_why()
    {
        var service = new FakeService();
        var host = new FakeHost { EnsureError = new InvalidOperationException("OMP is not installed") };
        RunSta((window, control) =>
        {
            Send(window, "first");
            Assert.Equal("first", Named<TextBox>(window, "Prompt").Text);
            Assert.True(HasText(window, "Prompt failed: OMP is not installed"));

            host.EnsureError = null;
            service.PromptError = new InvalidOperationException("pipe closed");
            Send(window, "second");
            Assert.Equal("second", Named<TextBox>(window, "Prompt").Text);
            Assert.True(HasText(window, "Prompt failed: pipe closed"));

            service.PromptError = null;
            service.PromptWith = async () =>
            {
                await Task.Delay(100);

                throw new InvalidOperationException("lost mid-turn");
            };
            Send(window, "third");
            Pump(300);
            Assert.True(HasText(window, "Prompt failed: lost mid-turn"));

            service.PromptWith = async () =>
            {
                await Task.Delay(100);

                throw new OmpSupersededException();
            };
            Send(window, "fourth");
            Pump(300);
            service.PromptWith = () => Task.FromException<PromptOutcome>(new OmpSupersededException());
            Send(window, "fifth");
            Assert.DoesNotContain(host.Errors, error => error.Error is OmpSupersededException);
        }, service, host);
    }

    [Fact]
    public void Actions_cancelled_by_a_restart_report_nothing()
    {
        var service = new FakeService();
        var host = new FakeHost { EnsureError = new OmpSupersededException() };
        RunSta((window, control) =>
        {
            var fast = Named<ToggleButton>(window, "Fast mode");
            fast.IsChecked = true;
            Click(fast);
            Pump(300);
            Assert.Empty(host.Errors);
            Assert.Empty(service.FastModes);
        }, service, host);
    }

    [Fact]
    public void Ctrl_and_the_wheel_zoom_the_chat_and_the_wheel_alone_scrolls() => RunSta((window, control) =>
                                                                                       {
                                                                                           var before = control.ZoomTransform.ScaleX;
                                                                                           var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                                                                                           control.RaiseEvent(wheel);
                                                                                           Assert.Equal(before, control.ZoomTransform.ScaleX);

                                                                                           control.Modifiers = () => ModifierKeys.Control;
                                                                                           control.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                                                                                           Assert.True(control.ZoomTransform.ScaleX > before);
                                                                                           control.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                                                                                           Assert.Equal(before, control.ZoomTransform.ScaleX, 3);
                                                                                       }, new FakeService(), new FakeHost());

    [Fact]
    public void The_conversation_tracks_keys_wheel_and_scroll_bar_drags_as_the_reader_scrolling()
    {
        var service = new FakeService { Transcript = Enumerable.Range(0, 40).Select(i => (TranscriptItem)new AssistantItem { Id = "a" + i, Text = "line " + i }).ToArray() };
        RunSta((window, control) =>
        {
            Pump(300);
            var scroller = control.Transcript.Scroller!;
            foreach (var key in new[] { Key.PageUp, Key.Up, Key.Home, Key.PageDown, Key.Down, Key.End, Key.Space, Key.A })
            {
                scroller.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(scroller)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            }

            scroller.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            var bar = Descendants(scroller).OfType<ScrollBar>().First();
            bar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            bar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            var text = Descendants(scroller).OfType<FrameworkElement>().First(e => e is RichTextBox);
            text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            text.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Pump();
            scroller.ScrollToTop();
            Pump(300);
            var jump = Descendants(window).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Jump to latest");
            Click(jump);
            Pump();
            Assert.Equal(Visibility.Collapsed, jump.Visibility);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_clipboard_that_refuses_the_copy_is_reported()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = "answer" } } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            control.SetClipboard = _ => throw new InvalidOperationException("clipboard busy");
            Click(Named<Button>(window, "Copy message"));
            Pump();
            Assert.True(HasText(window, "Copy failed: clipboard busy"));
        }, service, host);
    }

    [Fact]
    public void A_session_loaded_from_history_renders_its_markdown_like_the_live_view()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = "first session" } } };
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "first session"));
            service.RaiseReset(new TranscriptItem[]
            {
                new UserItem { Id = "h0", Text = "resume" },
                new AssistantItem { Id = "h1", Text = "### Plan\n\nDone with **bold**:\n\n| Name | Size |\n|---|---|\n| a | 1 |\n\n```cs\nvar x = 1;\n```", Model = "M" },
            });
            Pump(300);
            Assert.False(HasText(window, "first session"));
            Assert.True(HasText(window, "Plan"));
            var bolds = Descendants(window).OfType<RichTextBox>().SelectMany(r => r.Document.Blocks.OfType<Paragraph>()).SelectMany(p => p.Inlines.OfType<Bold>());
            Assert.Contains(bolds, b => new TextRange(b.ContentStart, b.ContentEnd).Text == "bold");
            Assert.True(HasText(window, "Size"));
            Assert.Contains(Descendants(window).OfType<TextBox>(), t => t.Text == "var x = 1;");
            Assert.False(HasText(window, "**bold**"));
        }, service, new FakeHost());
    }

    [Fact]
    public void File_mentions_skip_blank_paths_and_plus_without_an_open_file_adds_nothing() => RunSta((window, control) =>
                                                                                                    {
                                                                                                        control.AddFileMentions(new string[0]);
                                                                                                        control.AddFileMentions(null!);
                                                                                                        control.AddFileMentions(new[] { " ", "C:\\repo\\a.cs" });
                                                                                                        Pump();
                                                                                                        Assert.Single(AllNamed<FrameworkElement>(window, "Remove a.cs"));
                                                                                                    }, new FakeService(), new FakeHost());
}
