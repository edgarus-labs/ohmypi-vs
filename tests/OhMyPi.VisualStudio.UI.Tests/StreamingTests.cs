using Omp.Core;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>Streaming updates change the live item's view in place instead of rebuilding it.</summary>
[Collection("wpf")]
public sealed class StreamingTests
{
    private static RichTextBox Prose(System.Windows.DependencyObject root, string fragment) =>
        Descendants(root).OfType<RichTextBox>().Single(r => r.IsVisible && new TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.Contains(fragment));

    [Fact]
    public void A_streaming_answer_keeps_the_paragraphs_it_already_finished()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = "First paragraph.\n\nSecond", Streaming = true } } };
        RunSta((window, control) =>
        {
            var first = Prose(window, "First paragraph.");
            first.Selection.Select(first.Document.ContentStart, first.Document.ContentEnd);
            Task.Run(() => service.RaiseItem(new AssistantItem { Id = "a1", Text = "First paragraph.\n\nSecond paragraph grows", Streaming = true })).Wait();
            Pump(200);
            Assert.True(HasText(window, "Second paragraph grows"));
            Assert.Same(first, Prose(window, "First paragraph."));
            Assert.False(first.Selection.IsEmpty);
            Task.Run(() => service.RaiseItem(new AssistantItem { Id = "a1", Text = "First paragraph.\n\nSecond paragraph done.", Streaming = false })).Wait();
            Pump(200);
            Assert.Same(first, Prose(window, "First paragraph."));
            Assert.NotNull(Named<Button>(window, "Copy message"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Running_shell_output_grows_in_the_same_text_box()
    {
        var running = Tool("t1", "bash", "{\"command\":\"make\"}", ToolStatus.Running);
        running.Partial = "step 1";
        var service = new FakeService { Transcript = new TranscriptItem[] { running } };
        RunSta((window, control) =>
        {
            var output = Descendants(window).OfType<TextBox>().Single(t => t.IsVisible && t.Text == "step 1");
            var next = Tool("t1", "bash", "{\"command\":\"make\"}", ToolStatus.Running);
            next.Partial = "step 1\nstep 2";
            Task.Run(() => service.RaiseItem(next)).Wait();
            Pump(200);
            Assert.Equal("step 1\nstep 2", output.Text);
            Assert.True(output.IsVisible);
        }, service, new FakeHost());
    }
}
