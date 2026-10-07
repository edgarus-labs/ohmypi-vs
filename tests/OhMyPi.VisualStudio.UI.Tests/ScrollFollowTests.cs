using Omp.Core;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The conversation follows new output while the reader is at the bottom, also after scrolling away and back.</summary>
[Collection("wpf")]
public sealed class ScrollFollowTests
{
    private static TranscriptItem[] Conversation(int count) => [.. Enumerable.Range(0, count).SelectMany(i => new TranscriptItem[]
    {
        new UserItem { Id = "u" + i, Text = "question " + i },
        Tool("t" + i, "bash", "{\"command\":\"run " + i + "\"}", ToolStatus.Done, new ToolResultView { Text = string.Join("\n", Enumerable.Range(0, 12).Select(l => $"line {l} of {i}")) }),
        new AssistantItem { Id = "a" + i, Text = "answer " + i + "\n\nwith a second paragraph" },
    })];

    private static bool AtBottom(ScrollViewer scroller) => scroller.ScrollableHeight - scroller.VerticalOffset <= 1;

    /// <summary>One mouse wheel notch over the last visible tool output, as the reader scrolls over the conversation.</summary>
    private static void WheelOver(System.Windows.Window window, int delta)
    {
        var target = Descendants(window).OfType<TextBox>().Last(t => t.IsVisible && t.Text.Contains("line 11 of"));
        var preview = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.UIElement.PreviewMouseWheelEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent });
        }

        Pump(80);
    }

    [Fact]
    public void Following_survives_a_clicked_text_box_after_jumping_to_the_latest_output()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running }, Transcript = Conversation(15) };
        RunSta((window, control) =>
        {
            window.Activate();
            Pump(300);
            var scroller = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "PART_Scroller");
            for (var notch = 0; notch < 4; notch++)
            {
                WheelOver(window, 120);
            }

            Assert.False(AtBottom(scroller), $"wheeling up stayed at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            var clicked = Descendants(window).OfType<TextBox>().First(t => t.IsVisible && t.Text.Contains("line 11 of"));
            Assert.True(clicked.Focus(), "the output box did not take focus");
            Pump(200);
            Click(Named<Button>(window, "Jump to latest"));
            Pump(200);
            Assert.True(AtBottom(scroller), "jump to latest did not reach the bottom");
            clicked.BringIntoView();
            Pump(200);
            Assert.True(AtBottom(scroller), $"the clicked output box pulled the view to {scroller.VerticalOffset} of {scroller.ScrollableHeight}");

            for (var i = 100; i < 112; i++)
            {
                var next = i;
                Task.Run(() => service.RaiseItem(new AssistantItem { Id = "n" + next, Text = "new message " + next + "\n\nwith a second paragraph" })).Wait();
                Pump(300);
                Assert.True(AtBottom(scroller), $"new message {next} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            }
        }, service, new FakeHost());
    }

    [Fact]
    public void A_link_asking_to_be_shown_does_not_pull_the_view_or_fail()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running }, Transcript = Conversation(15).Concat(new TranscriptItem[] { new AssistantItem { Id = "link", Text = "see [the docs](https://example.com/docs)" } }).ToArray() };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Pump(300);
            var scroller = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "PART_Scroller");
            var link = Hyperlinks(window).Single(h => new System.Windows.Documents.TextRange(h.ContentStart, h.ContentEnd).Text == "the docs");
            for (var i = 100; i < 106; i++)
            {
                var next = i;
                Task.Run(() => service.RaiseItem(new AssistantItem { Id = "n" + next, Text = "new message " + next + "\n\nwith a second paragraph" })).Wait();
                Pump(150);
            }
            link.BringIntoView();
            Pump(200);
            Assert.Empty(host.Errors);
            Assert.True(AtBottom(scroller), $"the link pulled the view to {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
        }, service, host);
    }

    [Fact]
    public void Following_resumes_after_scrolling_up_a_little_and_wheeling_back_to_the_bottom()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running }, Transcript = Conversation(15) };
        RunSta((window, control) =>
        {
            Pump(300);
            var scroller = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "PART_Scroller");
            Assert.True(AtBottom(scroller), "the conversation did not open at the bottom");

            void Wheel(int delta)
            {
                var target = Descendants(window).OfType<TextBox>().Last(t => t.IsVisible && t.Text.Contains("line 11 of"));
                var preview = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.UIElement.PreviewMouseWheelEvent };
                target.RaiseEvent(preview);
                if (!preview.Handled)
                {
                    target.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent });
                }

                Pump(80);
            }

            Wheel(120);
            Assert.False(AtBottom(scroller), "wheeling up over a tool card did not scroll");
            for (var step = 0; step < 50 && !AtBottom(scroller); step++)
            {
                Wheel(-120);
            }

            Assert.True(AtBottom(scroller), "wheeling down did not reach the bottom");

            var text = "streaming";
            for (var i = 0; i < 15; i++)
            {
                text += "\n\nparagraph " + i + " with enough words to wrap across the width of the panel at least once or twice";
                var growing = new AssistantItem { Id = "live", Text = text, Streaming = true };
                Task.Run(() => service.RaiseItem(growing)).Wait();
                Pump(150);
                Assert.True(AtBottom(scroller), $"streamed paragraph {i} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            }

            for (var i = 100; i < 103; i++)
            {
                var next = i;
                Task.Run(() => service.RaiseItem(Tool("t" + next, "bash", "{\"command\":\"more\"}", ToolStatus.Done, new ToolResultView { Text = "a\nb\nc\nd\ne\nf" }))).Wait();
                Pump(300);
                Assert.True(AtBottom(scroller), $"new output {next} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            }
        }, service, new FakeHost());
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.5)]
    [InlineData(4.0)]
    public void Following_keeps_working_when_the_chat_is_zoomed_in(double zoom)
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running }, Transcript = Conversation(15) };
        RunSta((window, control) =>
        {
            Pump(300);
            control.ZoomTransform.ScaleX = zoom;
            control.ZoomTransform.ScaleY = zoom;
            Pump(300);
            var scroller = Descendants(window).OfType<ScrollViewer>().First(s => s.Name == "PART_Scroller");
            Assert.True(AtBottom(scroller), $"zooming to {zoom} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");

            var text = "streaming";
            for (var i = 0; i < 10; i++)
            {
                text += "\n\nparagraph " + i + " with enough words to wrap across the width of the panel at least once or twice";
                var growing = new AssistantItem { Id = "live", Text = text, Streaming = true };
                Task.Run(() => service.RaiseItem(growing)).Wait();
                Pump(150);
                Assert.True(AtBottom(scroller), $"at zoom {zoom} streamed paragraph {i} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            }

            for (var i = 100; i < 104; i++)
            {
                var next = i;
                Task.Run(() => service.RaiseItem(Tool("t" + next, "bash", "{\"command\":\"more\"}", ToolStatus.Done, new ToolResultView { Text = "a\nb\nc\nd\ne\nf" }))).Wait();
                Pump(300);
                Assert.True(AtBottom(scroller), $"at zoom {zoom} new output {next} left the view at {scroller.VerticalOffset} of {scroller.ScrollableHeight}");
            }
        }, service, new FakeHost());
    }
}
