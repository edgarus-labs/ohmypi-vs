using Omp.Core;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Documents;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>What an answer in Markdown shows and where its links lead.</summary>
[Collection("wpf")]
public sealed class MarkdownFlowsTests
{
    private static void Click(Hyperlink link) => link.RaiseEvent(new System.Windows.RoutedEventArgs(Hyperlink.ClickEvent));

    private static Hyperlink Link(System.Windows.Window window, string text) =>
        Hyperlinks(window).First(link => new TextRange(link.ContentStart, link.ContentEnd).Text == text);

    [Fact]
    public void Headings_lists_tables_and_emphasis_show_their_text()
    {
        const string answer = "### Plan\n#### Steps\n##### Note\n###### Small\n\n1. first *step*\n   - nested **bold**\n2. second\n\n| Name | Size |\n|:----:|-----:|\n| a | 1 | extra |\n\nplain line one\nline two";
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = answer } } };
        RunSta((window, control) =>
        {
            foreach (var text in new[] { "Plan", "Steps", "Note", "Small", "first step", "nested bold", "second", "Name", "Size", "extra", "plain line one" })
            {
                Assert.True(HasText(window, text), text);
            }

            Assert.True(HasText(window, "1."));
            Assert.True(HasText(window, "2."));
        }, service, new FakeHost());
    }

    [Fact]
    public void Web_links_open_in_the_browser_file_references_open_in_the_editor_and_other_links_stay_text()
    {
        const string answer = "See [docs](https://example.com/docs), [the file](src/a.cs#L3), `src/b.cs:7`, `not a file` and [mail](mailto:x@example.com).";
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omp-md-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "src"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "src", "a.cs"), "a");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "src", "b.cs"), "b");
        var service = new FakeService { Cwd = dir, Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = answer } } };
        var host = new FakeHost();
        try
        {
            RunSta((window, control) =>
            {
                var launched = new List<string>();
                control.LaunchUrl = launched.Add;
                Click(Link(window, "docs"));
                Click(Link(window, "the file"));
                Click(Link(window, "src/b.cs:7"));
                Pump();
                Assert.Equal(new[] { "https://example.com/docs" }, launched);
                Assert.Equal(2, host.Opened.Count);
                Assert.True(HasText(window, "mail (mailto:x@example.com)"));
                Assert.True(HasText(window, "not a file"));
            }, service, host);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Fenced_code_shows_lines_that_start_with_numbers_verbatim()
    {
        const string answer = "Log:\n\n```\n12:30:45 INFO start\n12:31:02 INFO done\n```\n\nSteps:\n\n```\n1-3: setup\n```";
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = answer } } };
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "12:30:45 INFO start"));
            Assert.True(HasText(window, "12:31:02 INFO done"));
            Assert.True(HasText(window, "1-3: setup"));
        }, service, new FakeHost());
    }
}
