using Newtonsoft.Json.Linq;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>File links, Diff links and expanded output of tool rows in the transcript.</summary>
[Collection("wpf")]
public sealed class ToolRowTests
{
    [Fact]
    public void Diff_is_offered_only_for_tracked_files_or_files_with_a_recorded_old_text()
    {
        var untracked = Tool("t1", "edit", "{\"path\":\"src/a.cs\"}", ToolStatus.Done, new ToolResultView { Text = "ok" });
        var service = new FakeService { Transcript = new TranscriptItem[] { untracked } };
        var host = new FakeHost();
        RunSta((window, control) => Assert.Empty(AllNamed<Button>(window, "Diff")), service, host);
    }

    [Fact]
    public void Diff_of_a_tracked_file_passes_the_raw_tool_path()
    {
        var item = Tool("t1", "edit", "{\"path\":\"src/a.cs\"}", ToolStatus.Done, new ToolResultView { Text = "ok" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost { Changes = new[] { new TrackedChange { Path = "C:\\repo\\src\\a.cs", Status = ChangeStatus.Modified } } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "Diff"));
            Pump();
            Assert.Equal(("src/a.cs", (string?)null), Assert.Single(host.DiffRequests));
        }, service, host);
    }

    [Fact]
    public void Diff_without_a_baseline_uses_the_old_text_the_tool_recorded()
    {
        var item = Tool("t1", "edit", "{\"path\":\"src/a.cs\"}", ToolStatus.Done,
            new ToolResultView { Text = "ok", Details = JToken.Parse("{\"path\":\"src/a.cs\",\"oldText\":\"before\\n\"}") });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "Diff"));
            Pump();
            Assert.Equal(("src/a.cs", (string?)"before\n"), Assert.Single(host.DiffRequests));
        }, service, host);
    }

    [Fact]
    public void File_links_pass_home_relative_paths_to_the_host_unresolved()
    {
        var item = Tool("t1", "read", "{\"path\":\"~/notes/todo.md\"}", ToolStatus.Done, new ToolResultView { Text = "x" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "~/notes/todo.md"));
            Pump();
            Assert.Equal(("~/notes/todo.md", (int?)null), Assert.Single(host.Opened));
        }, service, host);
    }

    [Fact]
    public void Multi_file_edits_link_every_file_and_diff_the_tracked_ones()
    {
        var item = Tool("t1", "edit", "{\"edits\":[{\"path\":\"src/a.cs\"},{\"path\":\"src/b.cs\"}]}", ToolStatus.Done, new ToolResultView { Text = "ok" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost { Changes = new[] { new TrackedChange { Path = "C:\\repo\\src\\b.cs", Status = ChangeStatus.Modified } } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "src/a.cs"));
            Click(Named<Button>(window, "src/b.cs"));
            Click(Assert.Single(AllNamed<Button>(window, "Diff src/b.cs")));
            Pump();
            Assert.Equal(new[] { "src/a.cs", "src/b.cs" }, host.Opened.Select(o => o.Path));
            Assert.Equal(("src/b.cs", (string?)null), Assert.Single(host.DiffRequests));
        }, service, host);
    }

    private static System.Collections.Generic.List<System.Windows.FrameworkElement> ToolRows(System.Windows.Window window) =>
        [.. Descendants(window).OfType<System.Windows.FrameworkElement>().Where(e => e.IsVisible && System.Windows.Automation.AutomationProperties.GetAutomationId(e) == "tool-row")];

    /// <summary>The visual of the only tool call on screen: no expanders and no card nested inside.</summary>
    private static System.Windows.FrameworkElement ToolRowOf(System.Windows.Window window)
    {
        var row = Assert.Single(ToolRows(window));
        Assert.DoesNotContain(Descendants(row).OfType<ToggleButton>(), t => t.IsVisible);

        return row;
    }

    private static TextBox Box(System.Windows.DependencyObject root, string name) =>
        Descendants(root).OfType<TextBox>().Single(t => t.IsVisible && System.Windows.Automation.AutomationProperties.GetName(t) == name);

    [Fact]
    public void Generic_tool_shows_its_arguments_and_output()
    {
        var item = Tool("t1", "custom_tool", "{\"query\":\"x\"}", ToolStatus.Done, new ToolResultView { Text = "result" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.Equal("query: x", Box(row, "Input").Text);
            Assert.Equal("result", Box(row, "Result").Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void Tool_input_is_shown_as_flat_lines_and_copying_it_whole_yields_the_json()
    {
        var item = Tool("t1", "custom_tool", "{\"query\":\"x\",\"limit\":5,\"scope\":{\"a\":1}}", ToolStatus.Done, new ToolResultView { Text = "result" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var box = Box(ToolRowOf(window), "Input");
            Assert.Equal("query: x\nlimit: 5\nscope: {\"a\":1}", box.Text);

            box.Focus();
            box.SelectAll();
            System.Windows.Clipboard.SetText("sentinel");
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.Equal("{\n  \"query\": \"x\",\n  \"limit\": 5,\n  \"scope\": {\n    \"a\": 1\n  }\n}", System.Windows.Clipboard.GetText());
        }, service, new FakeHost());
    }

    [Fact]
    public void A_job_started_in_the_background_is_not_shown_as_a_passed_call_and_has_no_made_up_duration()
    {
        var item = Tool("t1", "bash", "{\"command\":\"dotnet test\",\"async\":true}", ToolStatus.Done,
            new ToolResultView { Text = "Started in the background.", Details = JToken.Parse("{\"async\":{\"state\":\"running\",\"jobId\":\"bg_1\",\"type\":\"bash\"}}") });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            var icons = Descendants(row).OfType<TextBlock>().Where(t => t.IsVisible && t.Text.Length == 1).ToList();
            Assert.DoesNotContain(icons, t => t.Text == "\uE73E");
            Assert.Equal("\uE823", Assert.Single(icons).Text);
            var texts = Texts(row).ToList();
            Assert.Contains("in background", texts);
            Assert.DoesNotContain("12ms", texts);
            Assert.Equal("Started in the background.", Box(row, "Result").Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void Single_line_json_results_are_indented_and_copying_them_whole_yields_the_original_text()
    {
        const string raw = "{\"review_threads\":[],\"totalCount\":0}";
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = raw });
        var invalid = Tool("t2", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = "{\"a\":1,}" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item, invalid } };
        RunSta((window, control) =>
        {
            var rows = ToolRows(window);
            var box = Box(rows[0], "Result");
            Assert.Equal("{\n  \"review_threads\": [],\n  \"totalCount\": 0\n}", box.Text);
            Assert.Equal("{\"a\":1,}", Box(rows[1], "Result").Text);

            box.Focus();
            box.SelectAll();
            System.Windows.Clipboard.SetText("sentinel");
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.Equal(raw, System.Windows.Clipboard.GetText());

            box.Select(0, 3);
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.Equal("{\n ", System.Windows.Clipboard.GetText());
        }, service, new FakeHost());
    }

    [Fact]
    public void Copy_all_of_a_long_json_result_copies_the_original_text()
    {
        var raw = "[" + string.Join(",", Enumerable.Range(0, 3000).Select(i => i.ToString())) + "]";
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = raw });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "show more (3002 lines)"));
            Pump();
            System.Windows.Clipboard.SetText("sentinel");
            Click(Named<Button>(window, "Copy all 3002 lines"));
            Assert.Equal(raw, System.Windows.Clipboard.GetText());
        }, service, new FakeHost());
    }

    [Fact]
    public void Tool_output_previews_six_lines_then_offers_show_more()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line " + i));
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = text });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var output = Descendants(window).OfType<TextBox>().Single(t => t.IsVisible && t.Text.StartsWith("line 1\n", StringComparison.Ordinal));
            Assert.Equal(6, output.Text.Split('\n').Length);
            Assert.NotNull(Named<Button>(window, "show more (50 lines)"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Clicking_a_file_path_in_tool_output_opens_it_in_the_editor()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omp-click-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "src"));
        var file = System.IO.Path.Combine(root, "src", "a.cs");
        System.IO.File.WriteAllText(file, "x");
        try
        {
            var item = Tool("t1", "bash", "{\"command\":\"git diff\"}", ToolStatus.Done, new ToolResultView { Text = "edited src/a.cs:7 ok" });
            var service = new FakeService { Cwd = root, Transcript = new TranscriptItem[] { item } };
            var host = new FakeHost();
            RunSta((window, control) =>
            {
                var box = Descendants(window).OfType<TextBox>().Single(b => b.IsVisible && b.Text == "edited src/a.cs:7 ok");
                System.Windows.Point At(int index)
                {
                    var rect = box.GetRectFromCharacterIndex(index);

                    return new System.Windows.Point(rect.X + 2, rect.Y + rect.Height / 2);
                }
                Assert.False(OhMyPi.VisualStudio.UI.Views.FileClicks.OpenAt(box, At(2)));
                Assert.Empty(host.Opened);
                Assert.True(OhMyPi.VisualStudio.UI.Views.FileClicks.OpenAt(box, At(10)));
                Assert.Equal((file, (int?)7), Assert.Single(host.Opened));
            }, service, host);
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Show_more_caps_huge_output_and_offers_to_copy_all_of_it()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50_000).Select(i => "line " + i));
        var item = Tool("t1", "bash", "{\"command\":\"seq\"}", ToolStatus.Done, new ToolResultView { Text = text });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "show more (50000 lines)"));
            Pump();
            var shown = Descendants(window).OfType<TextBox>().Single(t => t.IsVisible && t.Text.Contains("line 50000"));
            Assert.True(shown.Text.Length < text.Length / 4, $"expanded output holds {shown.Text.Length} chars");
            Assert.NotNull(Named<Button>(window, "Copy all 50000 lines"));
        }, service, new FakeHost());
    }

    [Fact]
    public void The_turn_summary_names_its_scope_duration_tokens_and_cost()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new TurnSummaryItem { Id = "ts", DurationMs = 254_000, InputTokens = 21_000, OutputTokens = 1_800, CostUsd = 0.2548 } } };
        RunSta((window, control) =>
        {
            var summary = Descendants(window).OfType<TextBlock>().Single(t => t.IsVisible && t.Text.StartsWith("Whole turn", StringComparison.Ordinal));
            Assert.Equal("Whole turn · 4m 14s · 21k in · 1.8k out · $0.2548", summary.Text);
        }, service, new FakeHost());
    }
}
