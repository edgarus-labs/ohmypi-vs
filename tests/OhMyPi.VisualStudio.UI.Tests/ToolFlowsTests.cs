using Newtonsoft.Json.Linq;
using Omp.Core;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>What tool calls offer in the chat: links to agents and files, diffs with show more and copy all, and copying results.</summary>
[Collection("wpf")]
public sealed class ToolFlowsTests
{
    private static Button LinkNamed(DependencyObject root, string text) =>
        Descendants(root).OfType<Button>().First(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == text);

    private static string Diff(int pairs) =>
        "@@ -1 +1 @@\n" + string.Join("\n", Enumerable.Range(0, pairs).Select(i => $"-value = {i};\n+value = {i + 1};"));

    [Fact]
    public void A_task_call_links_to_its_agents_and_an_lsp_call_to_its_file()
    {
        var service = new FakeService
        {
            Transcript = new TranscriptItem[]
            {
                Tool("t1", "task", "{\"tasks\":[{\"id\":\"ReviewAuth\"},{\"name\":\"Docs\"}]}", ToolStatus.Done, new ToolResultView { Text = "ok" }),
                Tool("t2", "lsp", "{\"file\":\"src/a.cs\",\"line\":12}", ToolStatus.Done, new ToolResultView { Text = "found" }),
                Tool("t3", "debug", "{\"action\":\"continue\"}", ToolStatus.Done, new ToolResultView { Text = "running" }),
                Tool("t4", "mcp__github__get_me", "{}", ToolStatus.Done, new ToolResultView { Text = "me" }),
            },
        };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Click(LinkNamed(window, "• ReviewAuth"));
            Pump();
            Assert.True(HasText(window, "Agents"));
            Click(LinkNamed(window, "src/a.cs:12"));
            Pump();
            Assert.Contains(("src/a.cs", (int?)12), host.Opened);
            Assert.True(HasText(window, "mcp__github__get_me"));
        }, service, host);
    }

    [Fact]
    public void A_specialized_view_that_fails_falls_back_to_raw_arguments_and_is_logged()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { Tool("t1", "lsp", "{\"file\":\"a.cs\",\"line\":99999999999}", ToolStatus.Done, new ToolResultView { Text = "x" }) } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "Specialized view failed"));
            Assert.Contains(host.Errors, error => error.Message == "Showing the lsp tool row failed");
        }, service, host);
    }

    [Fact]
    public void A_long_command_and_extra_parameters_are_shown_under_a_shell_call()
    {
        var command = "dotnet test " + new string('x', 300);
        var service = new FakeService { Transcript = new TranscriptItem[] { Tool("t1", "bash", new JObject { ["command"] = command, ["timeout"] = 60 }.ToString(), ToolStatus.Done, new ToolResultView { Text = "ok" }) } };
        RunSta((window, control) =>
        {
            Assert.Equal(command, Named<TextBox>(window, "Command").Text);
            Assert.Contains("timeout", Named<TextBox>(window, "Parameters").Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_long_diff_previews_then_shows_more_and_past_the_limit_offers_copy_all()
    {
        var big = Diff(1100);
        var service = new FakeService { Transcript = new TranscriptItem[] { Tool("t1", "bash", "{\"command\":\"git diff\"}", ToolStatus.Done, new ToolResultView { Text = big }) } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            string copied = "";
            control.SetClipboard = text => copied = text;
            Click(LinkNamed(window, "show more (2201 lines)"));
            Pump();
            Assert.True(HasText(window, "Showing the first 2000 of 2201 lines."));
            Click(LinkNamed(window, "Copy all 2201 diff lines"));
            Pump();
            Assert.Equal(big, copied);
        }, service, host);
    }

    [Fact]
    public void An_edit_shows_its_diff_with_changed_words_and_short_diffs_show_whole()
    {
        var details = new JObject { ["diff"] = "@@ -1,3 +1,3 @@\n context\n-var a = 1;\n+var a = 2;\n-only removed\n\n" };
        var edit = Tool("t1", "edit", "{\"path\":\"C:\\\\repo\\\\a.cs\"}", ToolStatus.Done, new ToolResultView { Text = "ok", Details = details });
        var service = new FakeService { Transcript = new TranscriptItem[] { edit } };
        RunSta((window, control) =>
        {
            var box = Descendants(window).OfType<RichTextBox>().Single(r => r.IsVisible && r.Document.Blocks.Count > 2);
            var text = new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
            Assert.Contains("+var a = 2;", text);
            Assert.Contains("-only removed", text);
            Assert.DoesNotContain(Descendants(window).OfType<Button>(), b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("show more", StringComparison.Ordinal));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_huge_expanded_result_offers_copy_all()
    {
        var text = string.Join("\n", Enumerable.Range(0, 2500).Select(i => "line " + i));
        var service = new FakeService { Transcript = new TranscriptItem[] { Tool("t1", "grep", "{\"pattern\":\"x\"}", ToolStatus.Done, new ToolResultView { Text = text }) } };
        RunSta((window, control) =>
        {
            string copied = "";
            control.SetClipboard = value => copied = value;
            Click(LinkNamed(window, "show more (2500 lines)"));
            Pump();
            Click(LinkNamed(window, "Copy all 2500 lines"));
            Pump();
            Assert.Equal(text, copied);
        }, service, new FakeHost());
    }
}
