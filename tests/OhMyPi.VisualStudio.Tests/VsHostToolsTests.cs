using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.Logic;
using OhMyPi.VisualStudio.Logic.Automation;

namespace OhMyPi.VisualStudio.Tests;

internal sealed class RecordingVsAutomation : IVsAutomation
{
    public readonly List<string> Calls = new();
    public Exception? Failure;
    public CancellationToken LastToken;

    public SolutionInfo Solution = new();
    public List<DocumentInfo> Documents = new();
    public DocumentText Document = new();
    public List<string> Saved = new();
    public SelectionInfo? Selection;
    public BuildResult Build = new();
    public List<ErrorItem> Errors = new();
    public string Output = "";
    public DebugState State = new();
    public List<BreakpointInfo> Breakpoints = new();
    public int RemovedBreakpoints;
    public string Evaluation = "42";
    public List<string> CallStack = new();
    public List<LocalVariable> Locals = new();
    public List<string> Commands = new();

    private Task<T> Respond<T>(CancellationToken token, T result, string name, params object?[] args)
    {
        Record(token, name, args);
        return Task.FromResult(result);
    }

    private Task Record(CancellationToken token, string name, params object?[] args)
    {
        LastToken = token;
        Calls.Add($"{name}({string.Join(", ", args.Select(a => a == null ? "null" : a.ToString()))})");
        if (Failure != null) throw Failure;
        return Task.CompletedTask;
    }

    public Task<SolutionInfo> GetSolutionAsync(CancellationToken c) => Respond(c, Solution, "GetSolution");
    public Task<IReadOnlyList<DocumentInfo>> ListDocumentsAsync(CancellationToken c) => Respond<IReadOnlyList<DocumentInfo>>(c, Documents, "ListDocuments");
    public Task OpenDocumentAsync(string path, int? line, int? column, CancellationToken c) => Record(c, "OpenDocument", path, line, column);
    public Task<DocumentText> ReadDocumentAsync(string path, int? startLine, int? endLine, CancellationToken c) => Respond(c, Document, "ReadDocument", path, startLine, endLine);
    public Task ReplaceLinesAsync(string path, int startLine, int endLine, string text, CancellationToken c) => Record(c, "ReplaceLines", path, startLine, endLine, text);
    public Task<IReadOnlyList<string>> SaveDocumentsAsync(string? path, CancellationToken c) => Respond<IReadOnlyList<string>>(c, Saved, "SaveDocuments", path);
    public Task CloseDocumentAsync(string path, bool save, CancellationToken c) => Record(c, "CloseDocument", path, save);
    public Task<SelectionInfo?> GetSelectionAsync(CancellationToken c) => Respond(c, Selection, "GetSelection");
    public Task<BuildResult> BuildAsync(BuildAction action, string? project, string? configuration, CancellationToken c) => Respond(c, Build, "Build", action, project, configuration);
    public Task<IReadOnlyList<ErrorItem>> GetErrorsAsync(CancellationToken c) => Respond<IReadOnlyList<ErrorItem>>(c, Errors, "GetErrors");
    public Task<string> ReadOutputAsync(string? pane, int maxLines, CancellationToken c) => Respond(c, Output, "ReadOutput", pane, maxLines);
    public Task AddFileToProjectAsync(string project, string path, CancellationToken c) => Record(c, "AddFileToProject", project, path);
    public Task RemoveFileFromProjectAsync(string project, string path, CancellationToken c) => Record(c, "RemoveFileFromProject", project, path);
    public Task<DebugState> GetDebugStateAsync(CancellationToken c) => Respond(c, State, "GetDebugState");
    public Task<DebugState> DebugAsync(DebugAction action, CancellationToken c) => Respond(c, State, "Debug", action);
    public Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(CancellationToken c) => Respond<IReadOnlyList<BreakpointInfo>>(c, Breakpoints, "ListBreakpoints");
    public Task AddBreakpointAsync(string path, int line, string? condition, CancellationToken c) => Record(c, "AddBreakpoint", path, line, condition);
    public Task<int> RemoveBreakpointsAsync(string path, int? line, CancellationToken c) => Respond(c, RemovedBreakpoints, "RemoveBreakpoints", path, line);
    public Task<string> EvaluateAsync(string expression, CancellationToken c) => Respond(c, Evaluation, "Evaluate", expression);
    public Task<IReadOnlyList<string>> GetCallStackAsync(CancellationToken c) => Respond<IReadOnlyList<string>>(c, CallStack, "GetCallStack");
    public Task<IReadOnlyList<LocalVariable>> GetLocalsAsync(CancellationToken c) => Respond<IReadOnlyList<LocalVariable>>(c, Locals, "GetLocals");
    public Task<IReadOnlyList<string>> FindCommandsAsync(string filter, int max, CancellationToken c) => Respond<IReadOnlyList<string>>(c, Commands, "FindCommands", filter, max);
    public Task ExecuteCommandAsync(string command, string? arguments, CancellationToken c) => Record(c, "ExecuteCommand", command, arguments);
}

public class VsHostToolsTests
{
    private const string Repo = @"D:\work\repo";

    private readonly RecordingVsAutomation _vs = new();
    private readonly VsHostTools _tools;

    public VsHostToolsTests()
    {
        _tools = new VsHostTools(_vs, new WorkspaceScope(Repo, new[] { Repo }));
    }

    private Task<Omp.Core.HostToolResult> Invoke(string tool, string json) =>
        _tools.InvokeAsync(tool, JObject.Parse(json), CancellationToken.None);

    private async Task<string> Run(string tool, string json = "{}")
    {
        var result = await Invoke(tool, json);
        Assert.False(result.IsError);
        return result.Content;
    }

    private async Task<Exception> Fails(string tool, string json)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Invoke(tool, json));
        Assert.Empty(_vs.Calls);
        return failure;
    }

    private static string Quote(string path) => Newtonsoft.Json.JsonConvert.ToString(path);

    // ---- definitions ----------------------------------------------------------------------------------------------

    private static readonly string[] ToolNames =
    {
        "vs_solution", "vs_documents", "vs_open_document", "vs_read_document", "vs_replace_lines", "vs_save",
        "vs_close_document", "vs_selection", "vs_build", "vs_errors", "vs_output", "vs_add_file_to_project",
        "vs_remove_file_from_project", "vs_debug", "vs_breakpoints", "vs_debug_inspect", "vs_find_commands",
        "vs_execute_command",
    };

    [Fact]
    public void ProvidesTheDocumentedToolsWithUniqueNames()
    {
        var names = _tools.Definitions.Select(d => d.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(ToolNames.OrderBy(n => n), names.OrderBy(n => n));
        Assert.All(names, n => Assert.StartsWith("vs_", n));
    }

    [Fact]
    public void EveryDefinitionHasADescriptionThatSaysWhenToUseIt()
    {
        Assert.NotEmpty(_tools.Definitions);
        Assert.All(_tools.Definitions, d => Assert.True(d.Description.Length >= 30, d.Name));
    }

    [Fact]
    public void EveryParameterSchemaIsAClosedObjectSchemaWithDescribedProperties()
    {
        Assert.NotEmpty(_tools.Definitions);
        foreach (var definition in _tools.Definitions)
        {
            var schema = definition.Parameters;
            Assert.Equal("object", (string?)schema["type"]);
            Assert.Equal(false, (bool?)schema["additionalProperties"]);
            var properties = Assert.IsType<JObject>(schema["properties"]);
            foreach (var property in properties.Properties())
            {
                var body = Assert.IsType<JObject>(property.Value);
                Assert.True(!string.IsNullOrWhiteSpace((string?)body["description"]), $"{definition.Name}.{property.Name} description");
                Assert.True(body["type"] != null, $"{definition.Name}.{property.Name} type");
            }
            var required = Assert.IsType<JArray>(schema["required"]);
            foreach (var name in required) Assert.True(properties.ContainsKey((string)name!), $"{definition.Name} requires unknown {name}");
        }
    }

    [Theory]
    [InlineData("vs_solution", "")]
    [InlineData("vs_documents", "")]
    [InlineData("vs_open_document", "path")]
    [InlineData("vs_read_document", "path")]
    [InlineData("vs_replace_lines", "path,startLine,endLine,text")]
    [InlineData("vs_save", "")]
    [InlineData("vs_close_document", "path")]
    [InlineData("vs_selection", "")]
    [InlineData("vs_build", "")]
    [InlineData("vs_errors", "")]
    [InlineData("vs_output", "")]
    [InlineData("vs_add_file_to_project", "project,path")]
    [InlineData("vs_remove_file_from_project", "project,path")]
    [InlineData("vs_debug", "action")]
    [InlineData("vs_breakpoints", "action")]
    [InlineData("vs_debug_inspect", "kind")]
    [InlineData("vs_find_commands", "filter")]
    [InlineData("vs_execute_command", "command")]
    public void SchemasRequireExactlyTheMandatoryArguments(string tool, string required)
    {
        var schema = _tools.Definitions.Single(d => d.Name == tool).Parameters;
        var actual = ((JArray)schema["required"]!).Select(t => (string)t!).OrderBy(n => n);
        var expected = required.Length == 0 ? new string[0] : required.Split(',').OrderBy(n => n).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EnumeratedArgumentsAreDeclaredAsEnums()
    {
        JArray Enum(string tool, string property) =>
            (JArray)_tools.Definitions.Single(d => d.Name == tool).Parameters["properties"]![property]!["enum"]!;

        Assert.Equal(new[] { "build", "rebuild", "clean" }, Enum("vs_build", "action").Select(t => (string)t!));
        Assert.Equal(new[] { "error", "warning", "message", "all" }, Enum("vs_errors", "severity").Select(t => (string)t!));
        Assert.Equal(
            new[] { "start", "start_without_debugging", "stop", "break", "continue", "step_into", "step_over", "step_out", "state" },
            Enum("vs_debug", "action").Select(t => (string)t!));
        Assert.Equal(new[] { "list", "add", "remove" }, Enum("vs_breakpoints", "action").Select(t => (string)t!));
        Assert.Equal(new[] { "evaluate", "callstack", "locals" }, Enum("vs_debug_inspect", "kind").Select(t => (string)t!));
    }

    // ---- dispatch and calls ---------------------------------------------------------------------------------------

    public static TheoryData<string, string, string> ValidCalls => new()
    {
        { "vs_solution", "{}", "GetSolution()" },
        { "vs_documents", "{}", "ListDocuments()" },
        { "vs_open_document", "{'path':'src/a.cs','line':12,'column':3}", @"OpenDocument(D:\work\repo\src\a.cs, 12, 3)" },
        { "vs_open_document", "{'path':'src/a.cs'}", @"OpenDocument(D:\work\repo\src\a.cs, null, null)" },
        { "vs_read_document", "{'path':'src/a.cs','startLine':2,'endLine':4}", @"ReadDocument(D:\work\repo\src\a.cs, 2, 4)" },
        { "vs_read_document", "{'path':'src/a.cs'}", @"ReadDocument(D:\work\repo\src\a.cs, null, null)" },
        { "vs_replace_lines", "{'path':'src/a.cs','startLine':2,'endLine':3,'text':'x\\ny'}", "ReplaceLines(D:\\work\\repo\\src\\a.cs, 2, 3, x\ny)" },
        { "vs_replace_lines", "{'path':'src/a.cs','startLine':2,'endLine':1,'text':'new'}", @"ReplaceLines(D:\work\repo\src\a.cs, 2, 1, new)" },
        { "vs_replace_lines", "{'path':'src/a.cs','startLine':2,'endLine':3,'text':''}", @"ReplaceLines(D:\work\repo\src\a.cs, 2, 3, )" },
        { "vs_save", "{}", "SaveDocuments(null)" },
        { "vs_save", "{'path':'src/a.cs'}", @"SaveDocuments(D:\work\repo\src\a.cs)" },
        { "vs_close_document", "{'path':'src/a.cs'}", @"CloseDocument(D:\work\repo\src\a.cs, False)" },
        { "vs_close_document", "{'path':'src/a.cs','save':true}", @"CloseDocument(D:\work\repo\src\a.cs, True)" },
        { "vs_selection", "{}", "GetSelection()" },
        { "vs_build", "{}", "Build(Build, null, null)" },
        { "vs_build", "{'action':'rebuild','project':'App','configuration':'Release'}", "Build(Rebuild, App, Release)" },
        { "vs_build", "{'action':'clean'}", "Build(Clean, null, null)" },
        { "vs_errors", "{}", "GetErrors()" },
        { "vs_output", "{'pane':'Build','maxLines':50}", "ReadOutput(Build, 50)" },
        { "vs_add_file_to_project", "{'project':'App','path':'src/a.cs'}", @"AddFileToProject(App, D:\work\repo\src\a.cs)" },
        { "vs_remove_file_from_project", "{'project':'App','path':'src/a.cs'}", @"RemoveFileFromProject(App, D:\work\repo\src\a.cs)" },
        { "vs_debug", "{'action':'start'}", "Debug(Start)" },
        { "vs_debug", "{'action':'start_without_debugging'}", "Debug(StartWithoutDebugging)" },
        { "vs_debug", "{'action':'stop'}", "Debug(Stop)" },
        { "vs_debug", "{'action':'break'}", "Debug(BreakAll)" },
        { "vs_debug", "{'action':'continue'}", "Debug(Continue)" },
        { "vs_debug", "{'action':'step_into'}", "Debug(StepInto)" },
        { "vs_debug", "{'action':'step_over'}", "Debug(StepOver)" },
        { "vs_debug", "{'action':'step_out'}", "Debug(StepOut)" },
        { "vs_debug", "{'action':'state'}", "GetDebugState()" },
        { "vs_breakpoints", "{'action':'list'}", "ListBreakpoints()" },
        { "vs_breakpoints", "{'action':'add','path':'src/a.cs','line':10,'condition':'x > 1'}", @"AddBreakpoint(D:\work\repo\src\a.cs, 10, x > 1)" },
        { "vs_breakpoints", "{'action':'add','path':'src/a.cs','line':10}", @"AddBreakpoint(D:\work\repo\src\a.cs, 10, null)" },
        { "vs_breakpoints", "{'action':'remove','path':'src/a.cs','line':10}", @"RemoveBreakpoints(D:\work\repo\src\a.cs, 10)" },
        { "vs_breakpoints", "{'action':'remove','path':'src/a.cs'}", @"RemoveBreakpoints(D:\work\repo\src\a.cs, null)" },
        { "vs_debug_inspect", "{'kind':'evaluate','expression':'a + b'}", "Evaluate(a + b)" },
        { "vs_debug_inspect", "{'kind':'callstack'}", "GetCallStack()" },
        { "vs_debug_inspect", "{'kind':'locals'}", "GetLocals()" },
        { "vs_find_commands", "{'filter':'Format','max':5}", "FindCommands(Format, 5)" },
        { "vs_execute_command", "{'command':'Edit.FormatDocument'}", "ExecuteCommand(Edit.FormatDocument, null)" },
        { "vs_execute_command", "{'command':'File.OpenFile','arguments':'a.cs /e'}", "ExecuteCommand(File.OpenFile, a.cs /e)" },
    };

    [Fact]
    public void TheCallTableCoversEveryTool()
    {
        var covered = ValidCalls.Select(row => row.Data.Item1).Distinct().OrderBy(n => n);
        Assert.Equal(ToolNames.OrderBy(n => n), covered);
    }

    [Theory]
    [MemberData(nameof(ValidCalls))]
    public async Task EachToolCallsTheMatchingAutomationMethod(string tool, string json, string expected)
    {
        var result = await Invoke(tool, json);

        Assert.False(result.IsError);
        Assert.Equal(expected, Assert.Single(_vs.Calls));
    }

    [Theory]
    [MemberData(nameof(ValidCalls))]
    public async Task AutomationFailuresPropagate(string tool, string json, string expected)
    {
        _ = expected;
        _vs.Failure = new InvalidOperationException("boom");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(tool, json));

        Assert.Equal("boom", failure.Message);
    }

    [Fact]
    public async Task PassesTheCancellationTokenToTheAutomation()
    {
        using var cts = new CancellationTokenSource();

        await _tools.InvokeAsync("vs_solution", new JObject(), cts.Token);

        Assert.Equal(cts.Token, _vs.LastToken);
    }

    [Fact]
    public async Task RejectsAnUnknownTool()
    {
        var failure = await Fails("vs_format_the_planet", "{}");

        Assert.Contains("vs_format_the_planet", failure.Message);
    }

    // ---- argument validation --------------------------------------------------------------------------------------

    public static TheoryData<string, string, string> InvalidArguments => new()
    {
        { "vs_open_document", "{}", "path" },
        { "vs_open_document", "{'path':''}", "path" },
        { "vs_open_document", "{'path':5}", "path" },
        { "vs_open_document", "{'path':'a.cs','line':0}", "line" },
        { "vs_open_document", "{'path':'a.cs','line':'x'}", "line" },
        { "vs_open_document", "{'path':'a.cs','line':1.5}", "line" },
        { "vs_open_document", "{'path':'a.cs','line':2,'column':0}", "column" },
        { "vs_read_document", "{}", "path" },
        { "vs_read_document", "{'path':'a.cs','startLine':0}", "startLine" },
        { "vs_read_document", "{'path':'a.cs','endLine':'7'}", "endLine" },
        { "vs_read_document", "{'path':'a.cs','startLine':5,'endLine':4}", "endLine" },
        { "vs_replace_lines", "{'startLine':1,'endLine':1,'text':'x'}", "path" },
        { "vs_replace_lines", "{'path':'a.cs','endLine':1,'text':'x'}", "startLine" },
        { "vs_replace_lines", "{'path':'a.cs','startLine':0,'endLine':1,'text':'x'}", "startLine" },
        { "vs_replace_lines", "{'path':'a.cs','startLine':1,'text':'x'}", "endLine" },
        { "vs_replace_lines", "{'path':'a.cs','startLine':1,'endLine':1}", "text" },
        { "vs_replace_lines", "{'path':'a.cs','startLine':1,'endLine':1,'text':5}", "text" },
        { "vs_replace_lines", "{'path':'a.cs','startLine':5,'endLine':3,'text':'x'}", "endLine" },
        { "vs_save", "{'path':5}", "path" },
        { "vs_close_document", "{}", "path" },
        { "vs_close_document", "{'path':'a.cs','save':'yes'}", "save" },
        { "vs_build", "{'action':'bogus'}", "action" },
        { "vs_build", "{'project':5}", "project" },
        { "vs_build", "{'configuration':5}", "configuration" },
        { "vs_errors", "{'severity':'bogus'}", "severity" },
        { "vs_errors", "{'max':0}", "max" },
        { "vs_output", "{'maxLines':0}", "maxLines" },
        { "vs_output", "{'pane':5}", "pane" },
        { "vs_add_file_to_project", "{'path':'a.cs'}", "project" },
        { "vs_add_file_to_project", "{'project':'App'}", "path" },
        { "vs_remove_file_from_project", "{'path':'a.cs'}", "project" },
        { "vs_remove_file_from_project", "{'project':'App'}", "path" },
        { "vs_debug", "{}", "action" },
        { "vs_debug", "{'action':'explode'}", "action" },
        { "vs_breakpoints", "{}", "action" },
        { "vs_breakpoints", "{'action':'explode'}", "action" },
        { "vs_breakpoints", "{'action':'add','line':3}", "path" },
        { "vs_breakpoints", "{'action':'add','path':'a.cs'}", "line" },
        { "vs_breakpoints", "{'action':'add','path':'a.cs','line':0}", "line" },
        { "vs_breakpoints", "{'action':'add','path':'a.cs','line':3,'condition':5}", "condition" },
        { "vs_breakpoints", "{'action':'remove'}", "path" },
        { "vs_debug_inspect", "{}", "kind" },
        { "vs_debug_inspect", "{'kind':'explode'}", "kind" },
        { "vs_debug_inspect", "{'kind':'evaluate'}", "expression" },
        { "vs_find_commands", "{}", "filter" },
        { "vs_find_commands", "{'filter':'  '}", "filter" },
        { "vs_find_commands", "{'filter':'x','max':0}", "max" },
        { "vs_execute_command", "{}", "command" },
        { "vs_execute_command", "{'command':'X','arguments':5}", "arguments" },
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidArgumentsAreRefusedByName(string tool, string json, string argument)
    {
        var failure = await Fails(tool, json);

        Assert.Contains($"'{argument}'", failure.Message);
    }

    [Fact]
    public async Task EnumErrorsListTheAllowedValues()
    {
        var failure = await Fails("vs_errors", "{'severity':'bogus'}");

        Assert.Contains("error", failure.Message);
        Assert.Contains("warning", failure.Message);
        Assert.Contains("message", failure.Message);
        Assert.Contains("all", failure.Message);
        Assert.Contains("bogus", failure.Message);
    }

    [Fact]
    public async Task TreatsNullOptionalArgumentsAsAbsent()
    {
        await Run("vs_build", "{'action':null,'project':null,'configuration':null}");

        Assert.Equal("Build(Build, null, null)", Assert.Single(_vs.Calls));
    }

    // ---- workspace scope ------------------------------------------------------------------------------------------

    public static TheoryData<string, string> PathTools => new()
    {
        { "vs_open_document", "{'path':{PATH}}" },
        { "vs_read_document", "{'path':{PATH}}" },
        { "vs_replace_lines", "{'path':{PATH},'startLine':1,'endLine':1,'text':'x'}" },
        { "vs_save", "{'path':{PATH}}" },
        { "vs_close_document", "{'path':{PATH}}" },
        { "vs_add_file_to_project", "{'project':'App','path':{PATH}}" },
        { "vs_remove_file_from_project", "{'project':'App','path':{PATH}}" },
        { "vs_breakpoints", "{'action':'add','path':{PATH},'line':3}" },
        { "vs_breakpoints", "{'action':'remove','path':{PATH}}" },
    };

    [Theory]
    [MemberData(nameof(PathTools))]
    public async Task RefusesPathsOutsideTheWorkspaceWithoutCallingTheAutomation(string tool, string template)
    {
        foreach (var outside in new[] { @"C:\other\a.cs", @"..\other\a.cs", @"\\host\share\a.cs", @"D:\work\repository\a.cs" })
        {
            var failure = await Fails(tool, template.Replace("{PATH}", Quote(outside)));

            Assert.Contains("'path'", failure.Message);
            Assert.Contains("workspace", failure.Message);
        }
    }

    [Theory]
    [MemberData(nameof(PathTools))]
    public async Task RefusesPathsThatNameNoLocalFile(string tool, string template)
    {
        var failure = await Fails(tool, template.Replace("{PATH}", Quote("https://example.com/a.cs")));

        Assert.Contains("https://example.com/a.cs", failure.Message);
    }

    [Theory]
    [MemberData(nameof(PathTools))]
    public async Task ResolvesRelativePathsAgainstTheWorkingDirectory(string tool, string template)
    {
        await Run(tool, template.Replace("{PATH}", Quote("src/a.cs")));

        Assert.Contains(@"D:\work\repo\src\a.cs", Assert.Single(_vs.Calls));
    }

    [Theory]
    [MemberData(nameof(PathTools))]
    public async Task AcceptsAbsolutePathsInsideTheWorkspace(string tool, string template)
    {
        await Run(tool, template.Replace("{PATH}", Quote(@"D:\work\repo\lib\b.cs")));

        Assert.Contains(@"D:\work\repo\lib\b.cs", Assert.Single(_vs.Calls));
    }

    [Fact]
    public async Task ListingBreakpointsNeedsNoPath()
    {
        await Run("vs_breakpoints", "{'action':'list'}");

        Assert.Equal("ListBreakpoints()", Assert.Single(_vs.Calls));
    }

    // ---- defaults -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ErrorsDefaultToErrorsOnlyAndAtMostOneHundred()
    {
        for (var i = 0; i < 120; i++) _vs.Errors.Add(new ErrorItem { Severity = "error", Message = $"e{i}" });
        _vs.Errors.Add(new ErrorItem { Severity = "warning", Message = "w" });

        var text = await Run("vs_errors");

        var lines = text.Split('\n');
        Assert.Equal(100, lines.Count(l => l.StartsWith("error", StringComparison.Ordinal)));
        Assert.DoesNotContain("warning", text.Replace("warning(s)", ""));
        Assert.Contains("e99", text);
        Assert.DoesNotContain("e100", text);
        Assert.Contains("120", lines[0]);
        Assert.Contains("100", lines[0]);
    }

    [Theory]
    [InlineData("error", 1, 0, 0)]
    [InlineData("warning", 0, 1, 0)]
    [InlineData("message", 0, 0, 1)]
    [InlineData("all", 1, 1, 1)]
    public async Task ErrorsFilterBySeverity(string severity, int errors, int warnings, int messages)
    {
        _vs.Errors.Add(new ErrorItem { Severity = "error", Message = "an error" });
        _vs.Errors.Add(new ErrorItem { Severity = "warning", Message = "a warning" });
        _vs.Errors.Add(new ErrorItem { Severity = "message", Message = "a message" });

        var text = await Run("vs_errors", $"{{'severity':'{severity}'}}");

        Assert.Equal(errors, Count(text, "an error"));
        Assert.Equal(warnings, Count(text, "a warning"));
        Assert.Equal(messages, Count(text, "a message"));
    }

    private static int Count(string text, string needle) => text.Split('\n').Count(l => l.Contains(needle));

    [Fact]
    public async Task ErrorsHonorMax()
    {
        for (var i = 0; i < 10; i++) _vs.Errors.Add(new ErrorItem { Message = $"e{i}" });

        var text = await Run("vs_errors", "{'max':3}");

        Assert.Contains("e2", text);
        Assert.DoesNotContain("e3", text);
    }

    [Fact]
    public async Task ErrorsReportNothingFound()
    {
        _vs.Errors.Add(new ErrorItem { Severity = "warning", Message = "w" });

        Assert.Equal("No errors.", await Run("vs_errors"));
        Assert.Equal("No messages.", await Run("vs_errors", "{'severity':'message'}"));
    }

    [Fact]
    public async Task ErrorLinesCarryCodeLocationAndProject()
    {
        _vs.Errors.Add(new ErrorItem { Severity = "error", Code = "CS1002", Message = "; expected", Path = @"D:\work\repo\a.cs", Line = 3, Column = 5, Project = "App" });
        _vs.Errors.Add(new ErrorItem { Severity = "error", Code = "MSB4018", Message = "Task failed" });
        _vs.Errors.Add(new ErrorItem { Severity = "error", Message = "no code" });

        var lines = (await Run("vs_errors")).Split('\n');

        Assert.Contains(@"error CS1002 D:\work\repo\a.cs(3,5): ; expected [App]", lines);
        Assert.Contains("error MSB4018: Task failed", lines);
        Assert.Contains("error: no code", lines);
    }

    [Fact]
    public async Task OutputDefaultsToTwoHundredLinesAndCapsAtTwoThousand()
    {
        await Run("vs_output");
        await Run("vs_output", "{'maxLines':5000}");
        await Run("vs_output", "{'maxLines':2000}");
        await Run("vs_output", "{'maxLines':1}");

        Assert.Equal(new[] { "ReadOutput(null, 200)", "ReadOutput(null, 2000)", "ReadOutput(null, 2000)", "ReadOutput(null, 1)" }, _vs.Calls);
    }

    [Fact]
    public async Task FindCommandsDefaultsToFifty()
    {
        await Run("vs_find_commands", "{'filter':'Format'}");

        Assert.Equal("FindCommands(Format, 50)", Assert.Single(_vs.Calls));
    }

    // ---- results --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DescribesTheSolution()
    {
        _vs.Solution.Path = @"D:\work\repo\r.sln";
        _vs.Solution.ActiveConfiguration = "Debug";
        _vs.Solution.ActivePlatform = "Any CPU";
        _vs.Solution.StartupProject = "App";
        _vs.Solution.ActiveDocument = @"D:\work\repo\a.cs";
        _vs.Solution.Projects.Add(new ProjectInfo { Name = "App", Path = @"D:\work\repo\App\App.csproj" });
        _vs.Solution.Projects.Add(new ProjectInfo { Name = "Lib", Path = @"D:\work\repo\Lib\Lib.csproj" });

        var text = await Run("vs_solution");

        Assert.Contains(@"Solution: D:\work\repo\r.sln", text);
        Assert.Contains("Configuration: Debug|Any CPU", text);
        Assert.Contains("Startup project: App", text);
        Assert.Contains(@"Active document: D:\work\repo\a.cs", text);
        Assert.Contains("Projects (2)", text);
        Assert.Contains(@"- App: D:\work\repo\App\App.csproj", text);
        Assert.Contains(@"- Lib: D:\work\repo\Lib\Lib.csproj", text);
    }

    [Fact]
    public async Task SaysWhenNoSolutionIsOpen()
    {
        Assert.Equal("No solution is open.", await Run("vs_solution"));
    }

    [Fact]
    public async Task ListsOpenDocumentsWithTheirState()
    {
        _vs.Documents.Add(new DocumentInfo { Path = @"D:\work\repo\a.cs", IsActive = true, IsDirty = true });
        _vs.Documents.Add(new DocumentInfo { Path = @"D:\work\repo\b.cs", IsReadOnly = true });
        _vs.Documents.Add(new DocumentInfo { Path = @"D:\work\repo\c.cs" });

        var lines = (await Run("vs_documents")).Split('\n');

        Assert.Contains(@"D:\work\repo\a.cs [active] [unsaved]", lines);
        Assert.Contains(@"D:\work\repo\b.cs [read-only]", lines);
        Assert.Contains(@"D:\work\repo\c.cs", lines);
    }

    [Fact]
    public async Task SaysWhenNoDocumentIsOpen()
    {
        Assert.Equal("No documents are open.", await Run("vs_documents"));
    }

    [Fact]
    public async Task ReportsWhatWasOpened()
    {
        Assert.Equal(@"Opened D:\work\repo\a.cs.", await Run("vs_open_document", "{'path':'a.cs'}"));
        Assert.Equal(@"Opened D:\work\repo\a.cs at line 7.", await Run("vs_open_document", "{'path':'a.cs','line':7}"));
        Assert.Equal(@"Opened D:\work\repo\a.cs at line 7, column 2.", await Run("vs_open_document", "{'path':'a.cs','line':7,'column':2}"));
    }

    [Fact]
    public async Task ReadsDocumentsWithLineNumbers()
    {
        _vs.Document = new DocumentText { Path = @"D:\work\repo\a.cs", StartLine = 9, EndLine = 11, TotalLines = 40, Text = "nine\nten\neleven", FromEditor = true };

        var lines = (await Run("vs_read_document", "{'path':'a.cs','startLine':9,'endLine':11}")).Split('\n');

        Assert.Equal(@"D:\work\repo\a.cs (lines 9-11 of 40, editor buffer)", lines[0]);
        Assert.Equal(new[] { " 9: nine", "10: ten", "11: eleven" }, lines.Skip(1));
    }

    [Fact]
    public async Task ReadsDocumentsFromDiskAndIgnoresTheTrailingNewline()
    {
        _vs.Document = new DocumentText { Path = @"D:\work\repo\a.cs", StartLine = 1, EndLine = 2, TotalLines = 2, Text = "one\r\ntwo\r\n", FromEditor = false };

        var lines = (await Run("vs_read_document", "{'path':'a.cs'}")).Split('\n');

        Assert.Equal(@"D:\work\repo\a.cs (lines 1-2 of 2, disk)", lines[0]);
        Assert.Equal(new[] { "1: one", "2: two" }, lines.Skip(1));
    }

    [Fact]
    public async Task ReadsEmptyDocuments()
    {
        _vs.Document = new DocumentText { Path = @"D:\work\repo\a.cs", StartLine = 1, EndLine = 0, TotalLines = 0, Text = "" };

        var text = await Run("vs_read_document", "{'path':'a.cs'}");

        Assert.Equal(@"D:\work\repo\a.cs (lines 1-0 of 0, disk)" + "\n(no lines)", text);
    }

    [Fact]
    public async Task TruncatesLongDocumentsWithAMarker()
    {
        var text = string.Join("\n", Enumerable.Range(1, 20000).Select(i => $"line {i} of a very long file"));
        _vs.Document = new DocumentText { Path = @"D:\work\repo\a.cs", StartLine = 1, EndLine = 20000, TotalLines = 20000, Text = text };

        var result = await Run("vs_read_document", "{'path':'a.cs'}");

        Assert.True(result.Length < 45000, result.Length.ToString());
        Assert.Contains("[truncated", result);
        Assert.Contains("startLine", result);
        Assert.Contains("1: line 1 of a very long file", result);
        Assert.DoesNotContain("20000: ", result);
    }

    [Fact]
    public async Task ReportsAReplacementAndThatItIsUnsaved()
    {
        var replaced = await Run("vs_replace_lines", "{'path':'a.cs','startLine':2,'endLine':4,'text':'x'}");
        var inserted = await Run("vs_replace_lines", "{'path':'a.cs','startLine':5,'endLine':4,'text':'x'}");

        Assert.Contains(@"Replaced lines 2-4 of D:\work\repo\a.cs", replaced);
        Assert.Contains("vs_save", replaced);
        Assert.Contains(@"Inserted text before line 5 of D:\work\repo\a.cs", inserted);
        Assert.Contains("vs_save", inserted);
    }

    [Fact]
    public async Task ReportsWhatWasSaved()
    {
        Assert.Equal("Nothing to save.", await Run("vs_save"));

        _vs.Saved.AddRange(new[] { @"D:\work\repo\a.cs", @"D:\work\repo\b.cs" });
        var text = await Run("vs_save");

        Assert.Equal("Saved 2 document(s):\nD:\\work\\repo\\a.cs\nD:\\work\\repo\\b.cs", text);
    }

    [Fact]
    public async Task ReportsClosedDocuments()
    {
        Assert.Equal(@"Closed D:\work\repo\a.cs.", await Run("vs_close_document", "{'path':'a.cs'}"));
        Assert.Equal(@"Saved and closed D:\work\repo\a.cs.", await Run("vs_close_document", "{'path':'a.cs','save':true}"));
    }

    [Fact]
    public async Task DescribesTheSelection()
    {
        Assert.Equal("No active text editor.", await Run("vs_selection"));

        _vs.Selection = new SelectionInfo { Path = @"D:\work\repo\a.cs", StartLine = 3, StartColumn = 5, EndLine = 4, EndColumn = 9, Text = "foo\nbar" };
        Assert.Equal("D:\\work\\repo\\a.cs 3:5-4:9\nfoo\nbar", await Run("vs_selection"));

        _vs.Selection = new SelectionInfo { Path = @"D:\work\repo\a.cs", StartLine = 3, StartColumn = 5, EndLine = 3, EndColumn = 5 };
        Assert.Equal(@"D:\work\repo\a.cs caret at 3:5 (nothing selected)", await Run("vs_selection"));
    }

    [Fact]
    public async Task SummarizesASuccessfulBuild()
    {
        _vs.Build.Succeeded = true;
        _vs.Build.ProjectsSucceeded = 2;
        _vs.Build.ProjectsSkipped = 1;
        _vs.Build.Errors.Add(new ErrorItem { Severity = "warning", Code = "CS0168", Message = "unused", Path = @"D:\work\repo\a.cs", Line = 3, Column = 5, Project = "App" });

        var lines = (await Run("vs_build", "{'action':'rebuild'}")).Split('\n');

        Assert.Equal("Rebuild succeeded: 2 project(s) succeeded, 0 failed, 1 skipped.", lines[0]);
        Assert.Equal("Errors: 0, warnings: 1.", lines[1]);
        Assert.Equal(@"warning CS0168 D:\work\repo\a.cs(3,5): unused [App]", lines[2]);
    }

    [Fact]
    public async Task SummarizesAFailedBuildAndListsErrorsBeforeWarnings()
    {
        _vs.Build.ProjectsFailed = 1;
        _vs.Build.Errors.Add(new ErrorItem { Severity = "warning", Message = "w1" });
        _vs.Build.Errors.Add(new ErrorItem { Severity = "error", Message = "e1" });

        var lines = (await Run("vs_build")).Split('\n');

        Assert.Equal("Build failed: 0 project(s) succeeded, 1 failed, 0 skipped.", lines[0]);
        Assert.Equal("Errors: 1, warnings: 1.", lines[1]);
        Assert.Equal("error: e1", lines[2]);
        Assert.Equal("warning: w1", lines[3]);
    }

    [Fact]
    public async Task ListsAtMostFiftyBuildDiagnostics()
    {
        for (var i = 0; i < 60; i++) _vs.Build.Errors.Add(new ErrorItem { Severity = "error", Message = $"e{i}" });

        var lines = (await Run("vs_build", "{'action':'clean'}")).Split('\n');

        Assert.Equal(50, lines.Count(l => l.StartsWith("error", StringComparison.Ordinal)));
        Assert.Contains("Errors: 60, warnings: 0.", lines);
        Assert.Contains("… 10 more; use vs_errors to list them.", lines);
        Assert.StartsWith("Clean failed", lines[0]);
    }

    [Fact]
    public async Task ReportsTheOutputTextOrThatItIsEmpty()
    {
        Assert.Equal("The output pane is empty.", await Run("vs_output"));

        _vs.Output = "line1\nline2";
        Assert.Equal("line1\nline2", await Run("vs_output"));
    }

    [Fact]
    public async Task KeepsTheTailOfLongOutput()
    {
        _vs.Output = new string('x', 200000) + "\nTHE END";

        var text = await Run("vs_output");

        Assert.True(text.Length < 45000, text.Length.ToString());
        Assert.Contains("[truncated", text);
        Assert.EndsWith("THE END", text);
    }

    [Fact]
    public async Task ReportsFileMembership()
    {
        Assert.Equal(@"Added D:\work\repo\a.cs to project App.", await Run("vs_add_file_to_project", "{'project':'App','path':'a.cs'}"));
        Assert.Equal(@"Removed D:\work\repo\a.cs from project App.", await Run("vs_remove_file_from_project", "{'project':'App','path':'a.cs'}"));
    }

    [Fact]
    public async Task ReportsTheDebuggerState()
    {
        _vs.State = new DebugState { Mode = "break", Location = "Program.Main() line 12", Exception = "NullReferenceException: boom" };

        var text = await Run("vs_debug", "{'action':'step_over'}");

        Assert.Equal("Debugger mode: break\nLocation: Program.Main() line 12\nException: NullReferenceException: boom", text);
    }

    [Fact]
    public async Task ReportsADebuggerThatIsNotRunning()
    {
        Assert.Equal("Debugger mode: design", await Run("vs_debug", "{'action':'state'}"));
    }

    [Fact]
    public async Task ListsBreakpoints()
    {
        Assert.Equal("No breakpoints.", await Run("vs_breakpoints", "{'action':'list'}"));

        _vs.Breakpoints.Add(new BreakpointInfo { Path = @"D:\work\repo\a.cs", Line = 10, Enabled = true });
        _vs.Breakpoints.Add(new BreakpointInfo { Path = @"D:\work\repo\b.cs", Line = 4, Condition = "x > 3", Enabled = false });

        var lines = (await Run("vs_breakpoints", "{'action':'list'}")).Split('\n');

        Assert.Contains(@"D:\work\repo\a.cs:10", lines);
        Assert.Contains(@"D:\work\repo\b.cs:4 [condition: x > 3] [disabled]", lines);
    }

    [Fact]
    public async Task ReportsBreakpointChanges()
    {
        _vs.RemovedBreakpoints = 2;

        Assert.Equal(@"Breakpoint set at D:\work\repo\a.cs:10.", await Run("vs_breakpoints", "{'action':'add','path':'a.cs','line':10}"));
        Assert.Equal(@"Removed 2 breakpoint(s) at D:\work\repo\a.cs:10.", await Run("vs_breakpoints", "{'action':'remove','path':'a.cs','line':10}"));
        Assert.Equal(@"Removed 2 breakpoint(s) in D:\work\repo\a.cs.", await Run("vs_breakpoints", "{'action':'remove','path':'a.cs'}"));
    }

    [Fact]
    public async Task InspectsTheDebuggee()
    {
        Assert.Equal("42", await Run("vs_debug_inspect", "{'kind':'evaluate','expression':'x'}"));

        Assert.Equal("No stack frames.", await Run("vs_debug_inspect", "{'kind':'callstack'}"));
        _vs.CallStack.AddRange(new[] { "Program.Main() line 12", "Program.Run() line 4" });
        Assert.Equal("0: Program.Main() line 12\n1: Program.Run() line 4", await Run("vs_debug_inspect", "{'kind':'callstack'}"));

        Assert.Equal("No local variables.", await Run("vs_debug_inspect", "{'kind':'locals'}"));
        _vs.Locals.Add(new LocalVariable { Name = "x", Type = "int", Value = "5" });
        _vs.Locals.Add(new LocalVariable { Name = "s", Value = "\"hi\"" });
        Assert.Equal("x (int) = 5\ns = \"hi\"", await Run("vs_debug_inspect", "{'kind':'locals'}"));
    }

    [Fact]
    public async Task ListsMatchingCommands()
    {
        Assert.Equal("No commands match 'zzz'.", await Run("vs_find_commands", "{'filter':'zzz'}"));

        _vs.Commands.AddRange(new[] { "Edit.FormatDocument", "Edit.FormatSelection" });
        Assert.Equal("Edit.FormatDocument\nEdit.FormatSelection", await Run("vs_find_commands", "{'filter':'Format'}"));
    }

    [Fact]
    public async Task ReportsExecutedCommands()
    {
        Assert.Equal("Executed Edit.FormatDocument.", await Run("vs_execute_command", "{'command':'Edit.FormatDocument'}"));
        Assert.Equal("Executed File.OpenFile with arguments: a.cs /e.", await Run("vs_execute_command", "{'command':'File.OpenFile','arguments':'a.cs /e'}"));
    }
}
