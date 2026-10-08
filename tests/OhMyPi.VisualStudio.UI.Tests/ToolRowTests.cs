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

    /// <summary>The text of the colored code box named <paramref name="name"/>, with the line ends a rich text box reports normalized away.</summary>
    private static string Code(System.Windows.DependencyObject root, string name) =>
        new System.Windows.Documents.TextRange(Named<RichTextBox>(root, name).Document.ContentStart, Named<RichTextBox>(root, name).Document.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n');

    [Fact]
    public void Generic_tool_shows_its_arguments_and_its_output_without_opening()
    {
        var item = Tool("t1", "custom_tool", "{\"query\":\"x\"}", ToolStatus.Done, new ToolResultView { Text = "result" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.Equal("result", Box(row, "Result").Text);
            Assert.Equal("query: x", Code(row, "Input"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Tool_input_is_shown_as_flat_lines_and_copying_it_whole_yields_the_json()
    {
        var item = Tool("t1", "custom_tool", "{\"query\":\"x\",\"limit\":5,\"scope\":{\"a\":1}}", ToolStatus.Done, new ToolResultView { Text = "result" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.Equal("query: x\nlimit: 5\nscope: {\"a\":1}", Code(row, "Input"));
            var box = Named<RichTextBox>(row, "Input");

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
            var box = Named<RichTextBox>(rows[0], "Result");
            Assert.Equal("{\r\n  \"review_threads\": [],\r\n  \"totalCount\": 0\r\n}\r\n", new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text);
            Assert.Equal("{\"a\":1,}", Named<RichTextBox>(rows[1], "Result").Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single().Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).Aggregate(string.Concat));

            box.Focus();
            box.SelectAll();
            System.Windows.Clipboard.SetText("sentinel");
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.Equal(raw, System.Windows.Clipboard.GetText());

            box.Selection.Select(box.Document.ContentStart, box.Document.ContentStart.GetPositionAtOffset(3)!);
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.StartsWith("{", System.Windows.Clipboard.GetText());
            Assert.NotEqual(raw, System.Windows.Clipboard.GetText());
        }, service, new FakeHost());
    }

    [Fact]
    public void Json_results_color_only_their_keys_and_fenced_json_loses_its_fence()
    {
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = "{\"n\":1}\n```json\n{\n  \"n\": 1\n}\n```" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var box = Named<RichTextBox>(window, "Result");
            var runs = box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single().Inlines.OfType<System.Windows.Documents.Run>().ToList();
            Assert.Equal("{\"n\":1}\n{\n  \"n\": 1\n}", new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Replace("\r\n", "\n").TrimEnd('\n'));
            bool Colored(string text) => runs.First(r => r.Text == text).ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty) != System.Windows.DependencyProperty.UnsetValue;
            Assert.True(Colored("\"n\""));
            Assert.True(Colored("1"));
            Assert.False(Colored("{"));
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
    public void Tool_output_previews_five_lines_then_offers_show_more()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line " + i));
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = text });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var output = Descendants(window).OfType<TextBox>().Single(t => t.IsVisible && t.Text.StartsWith("line 1\n", StringComparison.Ordinal));
            Assert.Equal(5, output.Text.Split('\n').Length);
            Assert.NotNull(Named<Button>(window, "show more (50 lines)"));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_shell_call_with_an_intent_shows_it_in_the_header_and_the_command_below_it()
    {
        var item = Tool("t1", "bash", "{\"command\":\"git status --short\",\"i\":\"Checking the working tree\"}", ToolStatus.Done,
            new ToolResultView { Text = " M a.cs\n\nWall time: 0.05 seconds\n\nCommand exited with code 0\n", Details = JToken.Parse("{\"exitCode\":0}") });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.Contains("Checking the working tree", Texts(row));
            Assert.Equal(" M a.cs", Box(row, "Result").Text);
            Assert.Equal("git status --short", Code(row, "Command"));
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
            var shown = Descendants(window).OfType<TextBox>().Single(t => t.IsVisible && t.Text.StartsWith("line 1\nline 2\n", StringComparison.Ordinal));
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

    [Fact]
    public void Show_more_always_follows_the_text_it_cuts_and_in_IN_comes_after_the_parameters()
    {
        var command = string.Join("\n", Enumerable.Range(1, 6).Select(i => "echo " + i));
        var output = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line " + i));
        var item = Tool("t1", "bash", "{\"command\":" + Newtonsoft.Json.JsonConvert.ToString(command) + ",\"timeout\":60}", ToolStatus.Done, new ToolResultView { Text = output });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.True(Order(row, "show more (50 lines)") > Order(row, "Result"));
            Assert.True(Order(row, "show more (6 lines)") > Order(row, "Parameters"));
            Assert.True(Order(row, "Parameters") > Order(row, "Command"));
        }, service, new FakeHost());
    }
    [Fact]
    public void An_open_row_collapses_back_to_its_preview()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50).Select(i => "line " + i));
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = text });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "show more (50 lines)"));
            Pump();
            Assert.Equal(50, Box(ToolRowOf(window), "Result").Text.Split('\n').Length);
            Click(Named<Button>(window, "collapse"));
            Pump();
            Assert.Equal(5, Box(ToolRowOf(window), "Result").Text.Split('\n').Length);
            Assert.True(Named<Button>(window, "show more (50 lines)").IsVisible);
        }, service, new FakeHost());
    }

    private static string Listing(int lines) => "[src/a.cs#1A2B]\n" + string.Join("\n", Enumerable.Range(1, lines).Select(i => $"{i}:var x{i} = {i};"));

    [Fact]
    public void A_finished_read_shows_only_its_header_until_opened_and_collapses_back_to_it()
    {
        var item = Tool("t1", "read", "{\"path\":\"src/a.cs\"}", ToolStatus.Done, new ToolResultView { Text = Listing(12) });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Assert.Empty(AllNamed<RichTextBox>(window, "Result"));
            Click(Named<Button>(window, "show more (12 lines)"));
            Pump();
            Assert.StartsWith("var x1 = 1;", Code(window, "Result"));
            Click(Assert.Single(AllNamed<Button>(window, "collapse")));
            Pump();
            Assert.Empty(AllNamed<RichTextBox>(window, "Result"));
            Assert.True(Named<Button>(window, "show more (12 lines)").IsVisible);
        }, service, new FakeHost());
    }

    [Fact]
    public void An_opened_short_read_can_be_collapsed_back_to_its_header()
    {
        var item = Tool("t1", "read", "{\"path\":\"src/a.cs\"}", ToolStatus.Done, new ToolResultView { Text = Listing(3) });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "show more (3 lines)"));
            Pump();
            Assert.Equal("var x1 = 1;\nvar x2 = 2;\nvar x3 = 3;", Code(window, "Result"));
            Click(Assert.Single(AllNamed<Button>(window, "collapse")));
            Pump();
            Assert.Empty(AllNamed<RichTextBox>(window, "Result"));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_read_of_a_line_range_keeps_the_whole_selector_in_its_header_link()
    {
        var item = Tool("t1", "read", "{\"path\":\"src/a.cs:12-20\"}", ToolStatus.Done, new ToolResultView { Text = "x" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "src/a.cs:12-20"));
            Pump();
            Assert.Equal(("src/a.cs", (int?)12), Assert.Single(host.Opened));
        }, service, host);
    }

    [Fact]
    public void A_read_from_an_offset_names_the_line_in_its_header_link()
    {
        var item = Tool("t1", "read", "{\"path\":\"src/a.cs\",\"offset\":7}", ToolStatus.Done, new ToolResultView { Text = "x" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "src/a.cs:7"));
            Pump();
            Assert.Equal(("src/a.cs", (int?)7), Assert.Single(host.Opened));
        }, service, host);
    }

    [Fact]
    public void Search_matches_are_marked_in_a_small_grep_result()
    {
        const string result = "# src/Web/Features/\n\n## Status.cs#D5E9\n\n9:// happens here\n*10:// quality is never populated\n";
        var item = Tool("t1", "grep", "{\"pattern\":\"happens\"}", ToolStatus.Done, new ToolResultView { Text = result });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var runs = Named<RichTextBox>(window, "Result").Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single().Inlines.OfType<System.Windows.Documents.Run>();
            Assert.Equal(System.Windows.FontWeights.SemiBold, runs.Single(r => r.Text == "happens").FontWeight);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_grep_listing_that_opens_with_headers_keeps_its_numbers_in_the_gutter_in_the_preview()
    {
        var item = Tool("t1", "grep", "{\"pattern\":\"a\"}", ToolStatus.Done, new ToolResultView { Text = "# src/\n\n## a.cs\n\n9:alpha\n10:beta\n11:gamma\n12:delta" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var row = ToolRowOf(window);
            Assert.Equal("# src/\n\n## a.cs\n\nalpha", Code(row, "Result"));
            var gutter = Descendants(row).OfType<TextBlock>().Single(t => t.IsVisible && !t.IsHitTestVisible && t.Text.Contains("\n"));
            Assert.Equal(new[] { "", "", "", "", "9" }, gutter.Text.Split('\n').Select(n => n.Trim()));
        }, service, new FakeHost());
    }

    [Fact]
    public void Copying_a_whole_listing_yields_the_code_it_shows()
    {
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = "1:alpha\n2:beta\n" });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            var box = Named<RichTextBox>(window, "Result");
            box.Focus();
            box.SelectAll();
            System.Windows.Clipboard.SetText("sentinel");
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, box);
            Assert.Equal("alpha\nbeta", System.Windows.Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n'));
        }, service, new FakeHost());
    }

    [Fact]
    public void An_open_markdown_result_withholds_nothing_and_offers_no_copy_all()
    {
        var text = "# Title\n" + string.Join("\n", Enumerable.Range(1, 9).Select(i => "line " + i));
        var item = Tool("t1", "custom_tool", "{}", ToolStatus.Done, new ToolResultView { Text = text });
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Click(Named<Button>(window, "show more (10 lines)"));
            Pump();
            Assert.True(Named<Button>(window, "collapse").IsVisible);
            Assert.Empty(AllNamed<Button>(window, "Copy all 10 lines"));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_running_tool_shows_its_OUT_label_only_once_output_arrives()
    {
        var item = Tool("t1", "custom_tool", "{\"q\":\"x\"}", ToolStatus.Running);
        var service = new FakeService { Transcript = new TranscriptItem[] { item } };
        RunSta((window, control) =>
        {
            Assert.DoesNotContain("OUT", Texts(ToolRowOf(window)));
            var next = Tool("t1", "custom_tool", "{\"q\":\"x\"}", ToolStatus.Running);
            next.Partial = "first";
            service.RaiseItem(next);
            Pump();
            Assert.Contains("OUT", Texts(ToolRowOf(window)));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_selection_in_running_code_output_survives_an_update_that_leaves_the_preview_unchanged()
    {
        static ToolItem Running(int lines)
        {
            var next = Tool("t1", "grep", "{\"pattern\":\"hit\"}", ToolStatus.Running);
            next.Partial = string.Join("\n", Enumerable.Range(1, lines).Select(i => $"a.cs:{i}: hit {i}"));

            return next;
        }

        var service = new FakeService { Transcript = new TranscriptItem[] { Running(8) } };
        RunSta((window, control) =>
        {
            var box = Named<RichTextBox>(window, "Result");
            var start = box.Document.ContentStart.GetPositionAtOffset(2)!;
            box.Selection.Select(start, start.GetPositionAtOffset(4)!);
            var selected = box.Selection.Text;
            service.RaiseItem(Running(9));
            Pump();
            Assert.NotEqual("", selected);
            Assert.Equal(selected, box.Selection.Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_running_tool_whose_only_part_is_its_empty_output_shows_no_empty_card()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { Tool("t1", "bash", "{\"command\":\"ls\"}", ToolStatus.Running) } };
        RunSta((window, control) =>
        {
            System.Collections.Generic.IEnumerable<System.Windows.Controls.Border> Cards() =>
                Descendants(ToolRowOf(window)).OfType<System.Windows.Controls.Border>().Where(b => b.IsVisible && b.Child is System.Windows.FrameworkElement body && System.Windows.Automation.AutomationProperties.GetAutomationId(body) == "tool-body");

            Assert.Empty(Cards());
            var next = Tool("t1", "bash", "{\"command\":\"ls\"}", ToolStatus.Running);
            next.Partial = "a.txt";
            service.RaiseItem(next);
            Pump();
            Assert.Single(Cards());
        }, service, new FakeHost());
    }

    private static int Order(System.Windows.DependencyObject root, string automationName) =>
        DepthFirst(root).ToList().FindIndex(d => d is System.Windows.FrameworkElement fe && System.Windows.Automation.AutomationProperties.GetName(fe) == automationName);

    private static System.Collections.Generic.IEnumerable<System.Windows.DependencyObject> DepthFirst(System.Windows.DependencyObject node)
    {
        yield return node;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            foreach (var child in DepthFirst(System.Windows.Media.VisualTreeHelper.GetChild(node, i)))
            {
                yield return child;
            }
        }
    }
}
