using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class ToolFormatTests
{
    private static ToolItem Tool(string name, string? args = "{}", ToolStatus status = ToolStatus.Done, long? endedAt = null, ToolResultView? result = null, string? partial = null) =>
        new ToolItem
        {
            Id = "t1",
            Name = name,
            Args = args is null ? null : JToken.Parse(args),
            Status = status,
            StartedAt = 1000,
            EndedAt = endedAt,
            Result = result,
            Partial = partial,
        };

    private static ToolResultView Result(string text, bool isError = false, string? details = null) =>
        new ToolResultView { Text = text, IsError = isError, Details = details is null ? null : JToken.Parse(details) };

    [Theory]
    [InlineData("read", "File")]
    [InlineData("write", "File")]
    [InlineData("edit", "File")]
    [InlineData("ast_edit", "File")]
    [InlineData("grep", "Search")]
    [InlineData("glob", "Search")]
    [InlineData("ast_grep", "Search")]
    [InlineData("bash", "Shell")]
    [InlineData("eval", "Shell")]
    [InlineData("task", "Task")]
    [InlineData("lsp", "Lsp")]
    [InlineData("debug", "Lsp")]
    [InlineData("mcp__github_get_me", "Mcp")]
    [InlineData("job", "Generic")]
    [InlineData("await", "Generic")]
    [InlineData("cancel_job", "Generic")]
    [InlineData("brand_new_tool", "Generic")]
    [InlineData("", "Generic")]
    [InlineData("constructor", "Generic")]
    public void PickRenderer_maps_tool_families(string name, string expected) => Assert.Equal(expected, ToolFormat.PickRenderer(name).ToString());

    [Fact]
    public void SummarizeTool_uses_common_argument_keys()
    {
        Assert.Equal("src/a.ts", ToolFormat.SummarizeTool("read", JToken.Parse("{\"path\":\"src/a.ts\"}")));
        Assert.Equal("foo · src", ToolFormat.SummarizeTool("grep", JToken.Parse("{\"pattern\":\"foo\",\"path\":\"src\"}")));
        Assert.Equal("npm test", ToolFormat.SummarizeTool("bash", JToken.Parse("{\"command\":\"npm test\"}")));
        Assert.Equal("scout", ToolFormat.SummarizeTool("x", JToken.Parse("{\"agent\":\"scout\"}")));
    }

    [Fact]
    public void SummarizeTool_names_task_batches()
    {
        var args = JToken.Parse("{\"tasks\":[{\"name\":\"Alpha\",\"task\":\"...\"},{\"agent\":\"scout\",\"task\":\"...\"}]}");
        Assert.Equal("2 tasks: Alpha, scout", ToolFormat.SummarizeTool("task", args));
    }

    [Fact]
    public void SummarizeTool_is_single_line_and_bounded()
    {
        var s = ToolFormat.SummarizeTool("bash", new JObject { ["command"] = "echo a\necho b " + new string('x', 300) });
        Assert.DoesNotContain("\n", s);
        Assert.True(s.Length <= 120, s.Length.ToString());
        Assert.EndsWith("…", s);
    }

    [Fact]
    public void SummarizeTool_never_throws_on_unexpected_args()
    {
        Assert.Equal("", ToolFormat.SummarizeTool("x", null));
        Assert.Equal("", ToolFormat.SummarizeTool("x", JValue.CreateNull()));
        Assert.Equal("raw string", ToolFormat.SummarizeTool("x", new JValue("raw string")));
        Assert.Equal("42", ToolFormat.SummarizeTool("x", new JValue(42)));
        Assert.Equal("true", ToolFormat.SummarizeTool("x", new JValue(true)));
        Assert.Equal("", ToolFormat.SummarizeTool("x", JToken.Parse("{\"nested\":{\"deep\":1}}")));
        Assert.Equal("only string", ToolFormat.SummarizeTool("x", JToken.Parse("{\"title\":\"only string\"}")));
        Assert.Equal("a, b", ToolFormat.SummarizeTool("x", JToken.Parse("{\"paths\":[\"a\",\"b\"]}")));
    }

    [Fact]
    public void SplitMcpName_splits_server_and_tool()
    {
        Assert.Equal(("github", "get_me"), ToolFormat.SplitMcpName("mcp__github__get_me"));
        Assert.Equal(("context7", "query_docs"), ToolFormat.SplitMcpName("mcp__context7_query_docs"));
        Assert.Equal(("solo", ""), ToolFormat.SplitMcpName("mcp__solo"));
    }

    [Theory]
    [InlineData("+added", "Add")]
    [InlineData("-removed", "Delete")]
    [InlineData("@@ -1 +1 @@", "Hunk")]
    [InlineData("+++ b/file", "Meta")]
    [InlineData("--- a/file", "Meta")]
    [InlineData(" ctx", "Context")]
    public void DiffLineClass_classifies_unified_diff_lines(string line, string expected) => Assert.Equal(expected, ToolFormat.DiffLineClass(line).ToString());

    [Fact]
    public void HitCount_reads_details_or_counts_lines()
    {
        Assert.Equal(3, ToolFormat.HitCount(Result("a\nb\n\nc\n")));
        Assert.Equal(7, ToolFormat.HitCount(Result("a", details: "{\"matchCount\":7}")));
        Assert.Null(ToolFormat.HitCount(null));
    }

    [Fact]
    public void ExitCodeOf_reads_defensively()
    {
        Assert.Equal(2, ToolFormat.ExitCodeOf(Result("", details: "{\"exitCode\":2}")));
        Assert.Null(ToolFormat.ExitCodeOf(Result("", details: "\"nope\"")));
        Assert.Null(ToolFormat.ExitCodeOf(null));
        Assert.Null(ToolFormat.ExitCodeOf(Result("", details: "{\"exitCode\":1e30}")));
    }

    private static void AssertHeadline(ToolHeadline head, string name, string primary, string glyph, string detail, string? error = null)
    {
        Assert.Equal(name, head.Name);
        Assert.Equal(primary, head.Primary);
        Assert.Equal(glyph, head.Glyph);
        Assert.Equal(detail, head.Detail);
        Assert.Equal(error, head.Error);
    }

    [Fact]
    public void Headline_of_finished_read()
    {
        var head = ToolFormat.Headline(Tool("read", "{\"path\":\"src/AuthService.cs\"}", endedAt: 1012, result: Result("...")), null);
        AssertHeadline(head, "read", "src/AuthService.cs", "✓", "12ms");
    }

    [Fact]
    public void Headline_shows_long_durations_in_minutes()
    {
        var head = ToolFormat.Headline(Tool("bash", "{\"command\":\"make\"}", endedAt: 1000 + 125_000, result: Result("")), null);
        Assert.Equal("2m 5s", head.Detail);
    }

    [Fact]
    public void Headline_quotes_search_patterns_and_reports_hits()
    {
        var head = ToolFormat.Headline(Tool("grep", "{\"pattern\":\"ValidateToken\",\"path\":\"src\"}", endedAt: 1005, result: Result("a", details: "{\"matchCount\":8}")), null);
        AssertHeadline(head, "grep", "\"ValidateToken\" in src", "✓", "8 hits · 5ms");
    }

    [Fact]
    public void Headline_of_failed_command_has_exit_code_and_first_error_line()
    {
        var head = ToolFormat.Headline(
            Tool("bash", "{\"command\":\"npm test\"}", ToolStatus.Error, 3000, Result("\n  npm ERR! test failed  \nmore detail", true, "{\"exitCode\":1}")),
            null);
        AssertHeadline(head, "bash", "npm test", "✗", "exit 1 · 2.0s", "npm ERR! test failed");
    }

    [Fact]
    public void Headline_of_running_tool_has_no_duration()
    {
        var head = ToolFormat.Headline(Tool("bash", "{\"command\":\"sleep 10\\necho done\"}", ToolStatus.Running, partial: "x"), null);
        AssertHeadline(head, "bash", "sleep 10 echo done", "…", "");
    }

    [Fact]
    public void Headline_counts_written_lines()
    {
        var head = ToolFormat.Headline(Tool("write", "{\"path\":\"a.txt\",\"content\":\"1\\n2\\n3\"}", endedAt: 1001), null);
        Assert.Equal("3 lines · 1ms", head.Detail);
        Assert.Equal("a.txt", head.Primary);
    }

    [Fact]
    public void Headline_shows_paths_inside_cwd_relative()
    {
        Assert.Equal("src/Auth.cs", ToolFormat.Headline(Tool("read", "{\"path\":\"/work/app/src/Auth.cs\"}", endedAt: 1001), "/work/app").Primary);
        Assert.Equal("\"x\" in src", ToolFormat.Headline(Tool("grep", "{\"pattern\":\"x\",\"path\":\"/work/app/src\"}", endedAt: 1001), "/work/app/").Primary);
        Assert.Equal("/work/application/a.cs", ToolFormat.Headline(Tool("read", "{\"path\":\"/work/application/a.cs\"}", endedAt: 1001), "/work/app").Primary);
        Assert.Equal("src\\a.cs", ToolFormat.Headline(Tool("read", "{\"path\":\"C:\\\\repo\\\\src\\\\a.cs\"}", endedAt: 1001), "C:\\repo").Primary);
    }

    [Theory]
    [InlineData("D:/dev/proj/src/a.cs")]
    [InlineData(@"d:\dev\proj\src\a.cs")]
    [InlineData(@"D:\DEV\Proj\src\a.cs")]
    public void Headline_shows_paths_inside_a_windows_cwd_relative_whatever_the_separator_or_case(string path)
    {
        var args = new JObject { ["path"] = path };
        var primary = ToolFormat.Headline(Tool("read", args.ToString(), endedAt: 1001), @"D:\dev\proj").Primary;
        Assert.Equal(path.Substring(@"D:\dev\proj\".Length), primary);
    }

    [Fact]
    public void Headline_names_mcp_tools_by_server_and_tool()
    {
        var head = ToolFormat.Headline(Tool("mcp__github__get_me", "{\"query\":\"me\"}"), null);
        Assert.Equal("github › get_me", head.Name);
        Assert.Equal("me", head.Primary);
    }

    [Fact]
    public void Headline_falls_back_to_argument_summary()
    {
        var head = ToolFormat.Headline(Tool("brand_new_tool", "{\"title\":\"something new\"}", endedAt: 2500), null);
        AssertHeadline(head, "brand_new_tool", "something new", "✓", "1.5s");
    }

    [Fact]
    public void Headline_never_throws_on_odd_arguments_and_bounds_error()
    {
        Assert.Equal("", ToolFormat.Headline(Tool("read", null), null).Primary);
        Assert.Equal("\"raw\"", ToolFormat.Headline(Tool("grep", "\"raw\""), null).Primary);
        var longError = ToolFormat.Headline(Tool("x", status: ToolStatus.Error, result: Result(new string('e', 500), true)), null).Error;
        Assert.NotNull(longError);
        Assert.True(longError!.Length <= 160);
        Assert.EndsWith("…", longError);
        Assert.Null(ToolFormat.Headline(Tool("x", status: ToolStatus.Error), null).Error);
    }

    [Fact]
    public void Headline_does_not_count_error_lines_of_failed_search_as_hits()
    {
        var head = ToolFormat.Headline(Tool("grep", "{\"pattern\":\"x(\"}", ToolStatus.Error, 1002, Result("Error: invalid regex\nUnterminated group", true)), null);
        Assert.Equal("2ms", head.Detail);
        Assert.Equal("Error: invalid regex", head.Error);
    }

    [Fact]
    public void FileLinkLabel_is_relative_inside_cwd_with_line()
    {
        Assert.Equal("src\\a.ts:12", ToolFormat.FileLinkLabel("D:\\dev\\repo\\src\\a.ts", 12, "D:\\dev\\repo"));
        Assert.Equal("src/a.ts", ToolFormat.FileLinkLabel("/home/me/repo/src/a.ts", null, "/home/me/repo/"));
        Assert.Equal("/etc/hosts:3", ToolFormat.FileLinkLabel("/etc/hosts", 3, "/home/me/repo"));
        Assert.Equal("src/a.ts", ToolFormat.FileLinkLabel("src/a.ts", null, null));
    }

    [Theory]
    [InlineData("src/a.ts:12", "src/a.ts", 12)]
    [InlineData("src/a.ts:12-40", "src/a.ts", 12)]
    [InlineData("src/a.ts:50+150", "src/a.ts", 50)]
    [InlineData("src/a.ts:5-16,960-973", "src/a.ts", 5)]
    [InlineData("src/a.ts:-60", "src/a.ts", null)]
    [InlineData("src/a.ts:raw", "src/a.ts", null)]
    [InlineData("src/a.ts:5-16:raw", "src/a.ts", 5)]
    [InlineData("src/a.ts:raw:2-4", "src/a.ts", 2)]
    [InlineData("C:\\repo\\a.ts:7", "C:\\repo\\a.ts", 7)]
    [InlineData("C:\\repo\\a.ts", "C:\\repo\\a.ts", null)]
    [InlineData("notes", "notes", null)]
    public void SplitSelector_extracts_path_and_first_line(string raw, string path, int? line)
    {
        var selector = ToolFormat.SplitSelector(raw);
        Assert.Equal(path, selector.Path);
        Assert.Equal(line, selector.Line);
    }

    [Fact]
    public void CollectDiff_joins_per_file_diffs_or_reads_diff()
    {
        Assert.Equal("--- a.cs\n+x", ToolFormat.CollectDiff(JToken.Parse("{\"perFileResults\":[{\"path\":\"a.cs\",\"diff\":\"+x\"},{\"path\":\"b.cs\"}]}")));
        Assert.Equal("-y", ToolFormat.CollectDiff(JToken.Parse("{\"diff\":\"-y\"}")));
    }

    [Theory]
    [InlineData("diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -1 +1 @@\n-a\n+b", true)]
    [InlineData("@@ -3,2 +3,2 @@\n ctx\n-a\n+b", true)]
    [InlineData("- item one\n- item two", false)]
    [InlineData("Passed 3\nFailed 0", false)]
    public void Output_counts_as_a_diff_when_it_has_a_git_header_or_a_hunk(string text, bool diff)
    {
        Assert.Equal(diff, ToolFormat.LooksLikeDiff(text));
        Assert.Null(ToolFormat.CollectDiff(JToken.Parse("[1]")));
        Assert.Null(ToolFormat.CollectDiff(null));
    }

    [Fact]
    public void ShellCommand_prefers_command_then_code()
    {
        Assert.Equal("ls", ToolFormat.ShellCommand(Tool("bash", "{\"command\":\"ls\"}")));
        Assert.Equal("print(1)", ToolFormat.ShellCommand(Tool("eval", "{\"code\":\"print(1)\"}")));
    }

    [Fact]
    public void FileTarget_prefers_args_then_details_and_first_changed_line()
    {
        var read = ToolFormat.FileTarget(Tool("read", "{\"path\":\"src/a.cs:12-20\"}"));
        Assert.Equal("src/a.cs", read!.Value.Path);
        Assert.Equal(12, read.Value.Line);
        var edit = ToolFormat.FileTarget(Tool("edit", "{}", result: Result("ok", details: "{\"path\":\"b.cs\",\"firstChangedLine\":40}")));
        Assert.Equal("b.cs", edit!.Value.Path);
        Assert.Equal(40, edit.Value.Line);
        Assert.Equal(7, ToolFormat.FileTarget(Tool("read", "{\"file\":\"c.cs\",\"offset\":7}"))!.Value.Line);
        Assert.Null(ToolFormat.FileTarget(Tool("read", "{}")));
    }

    [Fact]
    public void TaskNames_use_name_agent_id_or_index() => Assert.Equal(new[] { "Alpha", "scout", "x1", "#4" }, ToolFormat.TaskNames(JToken.Parse("[{\"name\":\"Alpha\"},{\"agent\":\"scout\"},{\"id\":\"x1\"},{}]")));

    [Fact]
    public void State_is_running_done_or_failed_from_the_tool_status_and_result()
    {
        Assert.Equal(ToolState.Running, ToolFormat.StateOf(Tool("bash", status: ToolStatus.Running)));
        Assert.Equal(ToolState.Done, ToolFormat.StateOf(Tool("bash", endedAt: 1002, result: Result("ok"))));
        Assert.Equal(ToolState.Failed, ToolFormat.StateOf(Tool("bash", status: ToolStatus.Error, endedAt: 1002, result: Result("no", isError: true))));
        Assert.Equal(ToolState.Failed, ToolFormat.StateOf(Tool("bash", endedAt: 1002, result: Result("no", isError: true))));
    }

    [Fact]
    public void A_started_background_job_is_not_a_finished_call()
    {
        var started = Tool("bash", "{\"command\":\"dotnet test\",\"async\":true}", endedAt: 1003, result: Result("Started in the background.", details: "{\"async\":{\"state\":\"running\",\"jobId\":\"bg_1\",\"type\":\"bash\"}}"));
        Assert.Equal(ToolState.Background, ToolFormat.StateOf(started));
        var head = ToolFormat.Headline(started, null);
        Assert.Equal(ToolState.Background, head.State);
        Assert.Equal("in background", head.Detail);
        Assert.Equal("dotnet test", head.Primary);

        var failedToStart = Tool("bash", "{\"command\":\"x\"}", status: ToolStatus.Error, endedAt: 1003, result: Result("denied", isError: true, details: "{\"async\":{\"state\":\"running\"}}"));
        Assert.Equal(ToolState.Failed, ToolFormat.StateOf(failedToStart));
        var finishedJob = Tool("bash", "{\"command\":\"x\"}", endedAt: 1003, result: Result("done", details: "{\"async\":{\"state\":\"completed\"}}"));
        Assert.Equal(ToolState.Done, ToolFormat.StateOf(finishedJob));
    }

    [Theory]
    [InlineData("git rev-parse HEAD", true)]
    [InlineData("a  b", false)]
    [InlineData("line one\nline two", false)]
    [InlineData("tab\there", false)]
    [InlineData(" padded", false)]
    public void A_command_is_in_the_header_only_when_the_header_shows_it_unchanged(string command, bool fits) => Assert.Equal(fits, ToolFormat.FitsHeader(command));

    [Fact]
    public void A_command_longer_than_the_header_summary_does_not_fit()
    {
        Assert.True(ToolFormat.FitsHeader(new string('x', ToolFormat.MaxSummary)));
        Assert.False(ToolFormat.FitsHeader(new string('x', ToolFormat.MaxSummary + 1)));
    }

    [Fact]
    public void Shell_parameters_other_than_the_command_are_listed()
    {
        Assert.Equal("", ToolFormat.ShellParameters(Tool("bash", "{\"command\":\"ls\"}")));
        Assert.Equal("timeout: 30\ncwd: src\nenv: {\"A\":\"1\"}", ToolFormat.ShellParameters(Tool("bash", "{\"command\":\"ls\",\"timeout\":30,\"cwd\":\"src\",\"env\":{\"A\":\"1\"}}")));
        Assert.Equal("language: python", ToolFormat.ShellParameters(Tool("eval", "{\"code\":\"print(1)\",\"language\":\"python\"}")));
        Assert.Equal("", ToolFormat.ShellParameters(Tool("bash", "\"ls\"")));
        Assert.Equal("", ToolFormat.ShellParameters(Tool("bash", null)));
    }

    [Fact]
    public void FlatArgs_lists_an_object_one_property_per_line_with_bare_strings()
    {
        var args = JToken.Parse("{\"path\":\"src/a.ts\",\"line\":12,\"dry\":false,\"ref\":null,\"opts\":{\"a\":[1,2]},\"ids\":[\"x\",\"y\"]}");
        Assert.Equal("path: src/a.ts\nline: 12\ndry: false\nref: null\nopts: {\"a\":[1,2]}\nids: [\"x\",\"y\"]", ToolFormat.FlatArgs(args));
    }

    [Fact]
    public void FlatArgs_keeps_the_lines_of_a_multi_line_string_indented_under_its_name() => Assert.Equal("code: print(1)\n  print(2)\nlanguage: py", ToolFormat.FlatArgs(JToken.Parse("{\"code\":\"print(1)\\nprint(2)\",\"language\":\"py\"}")));

    [Fact]
    public void FlatArgs_shows_anything_but_an_object_as_indented_json()
    {
        Assert.Equal("[\n  1,\n  2\n]", ToolFormat.FlatArgs(JToken.Parse("[1,2]")));
        Assert.Equal("\"ls\"", ToolFormat.FlatArgs(JToken.Parse("\"ls\"")));
    }

    [Fact]
    public void Headline_of_a_shell_call_shows_its_intent_instead_of_the_command()
    {
        var head = ToolFormat.Headline(Tool("bash", "{\"command\":\"git status --short && git log -3\",\"i\":\"Checking working tree state\"}", endedAt: 1012, result: Result("ok")), null);
        Assert.Equal("Checking working tree state", head.Primary);
        Assert.Equal("Checking working tree state", ToolFormat.Description(Tool("bash", "{\"command\":\"ls\",\"i\":\"Checking working tree state\"}")));
        Assert.Equal("List files", ToolFormat.Description(Tool("eval", "{\"code\":\"ls\",\"title\":\"List files\"}")));
        Assert.Null(ToolFormat.Description(Tool("bash", "{\"command\":\"ls\"}")));
        Assert.Equal("ls", ToolFormat.Headline(Tool("bash", "{\"command\":\"ls\"}", endedAt: 1012), null).Primary);
    }

    [Fact]
    public void Shell_parameters_leave_out_the_intent_the_header_shows()
    {
        Assert.Equal("timeout: 30", ToolFormat.ShellParameters(Tool("bash", "{\"command\":\"ls\",\"i\":\"Listing\",\"timeout\":30}")));
        Assert.Equal("language: py", ToolFormat.ShellParameters(Tool("eval", "{\"code\":\"x\",\"title\":\"Run\",\"language\":\"py\"}")));
    }

    [Theory]
    [InlineData("a\nb\n\n\nWall time: 0.13 seconds\n\nCommand exited with code 1\n", "a\nb")]
    [InlineData("a\n\nWall time: 8.67 seconds", "a")]
    [InlineData("Command exited with code 127", "")]
    [InlineData("Wall time: 1 second\nreal output", "Wall time: 1 second\nreal output")]
    [InlineData("plain\n", "plain")]
    public void Shell_output_is_shown_without_its_timing_and_exit_code_trailer(string text, string shown) => Assert.Equal(shown, ToolFormat.StripShellTrailer(text));

    [Theory]
    [InlineData("{\"n\":0}\n```json\n{\n  \"n\": 0\n}\n```", "{\"n\":0}\n{\n  \"n\": 0\n}", "json")]
    [InlineData("```\nls\n```\n", "ls\n", null)]
    [InlineData("```\nx\n```\n```c#\ny\n```", "x\ny", "c#")]
    [InlineData("a ``` b\nnot a fence", "a ``` b\nnot a fence", null)]
    [InlineData("plain", "plain", null)]
    public void Markdown_fences_around_tool_output_are_not_shown_and_name_its_language(string text, string shown, string? language) =>
        Assert.Equal((shown, language), ToolFormat.StripFences(text));

    [Fact]
    public void The_language_of_a_shell_command_is_the_one_eval_names_or_bash()
    {
        Assert.Equal("py", ToolFormat.ShellLanguage(Tool("eval", "{\"code\":\"x\",\"language\":\"py\"}")));
        Assert.Null(ToolFormat.ShellLanguage(Tool("eval", "{\"code\":\"x\"}")));
        Assert.Equal("bash", ToolFormat.ShellLanguage(Tool("bash", "{\"command\":\"ls\"}")));
        Assert.Null(ToolFormat.ShellLanguage(Tool("read", "{\"path\":\"a\"}")));
    }

    [Theory]
    [InlineData("# mcp__create_issue - edgarus/create_issue\n\nCreate an issue.\n\n## Schema\ntype Args = {", true)]
    [InlineData("Intro line.\n\n## Section\n- one\n- two", true)]
    [InlineData("{\"a\":1}", false)]
    [InlineData("Passed 3\nFailed 0", false)]
    [InlineData("# only a heading", false)]
    [InlineData("x = 1 # a comment\ny = 2", false)]
    public void Tool_output_reads_as_markdown_when_it_has_a_heading_and_prose(string text, bool markdown) =>
        Assert.Equal(markdown, ToolFormat.LooksLikeMarkdown(text));

    /// <summary>
    /// The grep style result.
    /// </summary>
    private const string GrepStyleResult = "# src/Web/Features/\n\n## Status.cs#D5E9\n\n9:// happens here\n*10:// quality is never populated\n";

    [Theory]
    [InlineData("mcp__github_create_issue", true)]
    [InlineData("custom_tool", true)]
    [InlineData("grep", false)]
    [InlineData("read", false)]
    [InlineData("glob", false)]
    [InlineData("bash", false)]
    [InlineData("edit", false)]
    public void Only_text_tools_render_a_markdown_result_as_prose_while_file_and_shell_output_stays_code(string tool, bool prose) =>
        Assert.Equal(prose, ToolFormat.RendersMarkdown(Tool(tool, "{}", result: Result(GrepStyleResult))));

    [Theory]
    [InlineData("read", "{\"path\":\"src/App/Factory.cs\"}", "cs")]
    [InlineData("read", "{\"path\":\"C:\\\\repo\\\\build.PS1\"}", "ps1")]
    [InlineData("edit", "{\"path\":\"a/b.csproj\"}", "csproj")]
    [InlineData("write", "{\"file_path\":\"notes.md\"}", "md")]
    [InlineData("read", "{\"path\":\"Makefile\"}", null)]
    [InlineData("read", "{}", null)]
    [InlineData("read", "{\"path\":\"docs/README.md:1-40\"}", "md")]
    [InlineData("read", "{\"path\":\"a.md:raw\"}", "md")]
    [InlineData("read", "{\"path\":\"x.cs:5-16,960-973\"}", "cs")]
    [InlineData("bash", "{\"command\":\"cat a.cs\"}", null)]
    public void A_file_tools_result_takes_its_language_from_the_files_extension(string tool, string args, string? language) =>
        Assert.Equal(language, ToolFormat.FileLanguage(Tool(tool, args)));

    [Fact]
    public void Line_numbers_omp_prefixes_to_a_read_are_split_into_a_gutter_and_clean_code()
    {
        var text = "[src/a.cs#8257]\n1:using A;\n2:\n3:var x = 1;\n26-35:    { … }\n36:";
        var listing = ToolFormat.SplitLineNumbers(text)!;
        Assert.Equal("[src/a.cs#8257]", listing.Header);
        Assert.Equal(new[] { "1", "2", "3", "26-35", "36" }, listing.Numbers);
        Assert.Equal("using A;\n\nvar x = 1;\n    { … }\n", listing.Code);

        var trailer = ToolFormat.SplitLineNumbers("1:using A;\n…\n\n[truncated at 2 of 9 lines]")!;
        Assert.Equal(new[] { "1", "", "", "" }, trailer.Numbers);
        Assert.Equal("using A;\n…\n\n[truncated at 2 of 9 lines]", trailer.Code);

        var lead = ToolFormat.SplitLineNumbers("23\n----- 21409\n21406:    internal void M()\n21407:    {\n21408:    }")!;
        Assert.Equal(new[] { "", "", "21406", "21407", "21408" }, lead.Numbers);
        Assert.Equal("23\n----- 21409\n    internal void M()\n    {\n    }", lead.Code);

        Assert.Equal(new[] { "1" }, ToolFormat.SplitLineNumbers("1:using A;")!.Numbers);
    }

    [Theory]
    [InlineData("using A;\nvar x = 1;")]
    [InlineData("a\n2:x")]
    [InlineData("count\n1:a\nb\nc\nd")]
    [InlineData("")]
    public void Output_that_is_mostly_unnumbered_is_not_a_listing(string text) => Assert.Null(ToolFormat.SplitLineNumbers(text));

    [Theory]
    [InlineData("10:15:01 Build started\nRestoring packages...\nBuild succeeded.")]
    [InlineData("12:30:45 INFO start\n12:31:02 INFO done")]
    public void Output_whose_lines_start_with_timestamps_is_not_a_listing(string text) => Assert.Null(ToolFormat.SplitLineNumbers(text));

    [Fact]
    public void Only_an_omp_path_header_lets_a_sparsely_numbered_text_count_as_a_listing() =>
        Assert.Null(ToolFormat.SplitLineNumbers("['a', 'b']\nprocessing items\nwrote report\nsee below\n3: failed item"));

    [Fact]
    public void A_read_of_a_markdown_file_renders_as_prose_without_the_line_number_prefixes()
    {
        const string numbered = "[docs/a.md#1A2B]\n1:Intro text.\n2:- item\n";
        Assert.True(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"docs/a.md\"}", result: Result(numbered))));
        Assert.True(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"docs/a.md:1-40\"}", result: Result(numbered))));
        Assert.False(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"docs/a.cs\"}", result: Result(GrepStyleResult))));
        Assert.False(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"docs/a.md\"}", result: Result(numbered, isError: true))));
        Assert.Equal("Intro text.\n- item", ToolFormat.ProseText(numbered));
        Assert.True(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"xd://vs_find_commands\"}", result: Result("# vs_find_commands\n\nSearch command names.\n"))));
        Assert.False(ToolFormat.RendersMarkdown(Tool("read", "{\"path\":\"xd://log\"}", result: Result("plain line\nanother\n"))));
    }

    [Fact]
    public void A_search_tools_pattern_marks_its_result_as_a_regex_or_as_literal_text_when_invalid()
    {
        Assert.Equal("MapGet(", ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"Map(Get|Group)\\\\(\"}"))!.Match("x.MapGet(1)").Value);
        Assert.Equal("a(b", ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"a(b\"}"))!.Match("xa(by").Value);
        Assert.Null(ToolFormat.SearchPattern(Tool("grep", "{}")));
        Assert.Null(ToolFormat.SearchPattern(Tool("bash", "{\"pattern\":\"x\"}")));
        Assert.Equal("# plain", ToolFormat.ProseText("# plain"));
    }

    [Fact]
    public void A_grep_with_case_false_marks_its_pattern_ignoring_case()
    {
        Assert.Matches(ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"todo\",\"case\":false}"))!, "// TODO");
        Assert.Matches(ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"a(b\",\"case\":false}"))!, "A(B");
        Assert.DoesNotMatch(ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"todo\"}"))!, "// TODO");
        Assert.DoesNotMatch(ToolFormat.SearchPattern(Tool("grep", "{\"pattern\":\"todo\",\"case\":true}"))!, "// TODO");
    }
}
