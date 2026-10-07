using OhMyPi.VisualStudio.Logic.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        if (Failure is not null)
        {
            throw Failure;
        }

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
