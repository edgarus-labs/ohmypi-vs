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

    /// <summary>
    /// Asynchronously retrieves the output content from the specified pane, limited to the maximum number of lines.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="maxLines">The max lines.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string.</returns>
    public Task<string> ReadOutputAsync(string? pane, int maxLines, CancellationToken c) => Respond(c, Output, "ReadOutput", pane, maxLines);

    /// <summary>
    /// Asynchronously records the addition of a specified file path to a project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The path.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task AddFileToProjectAsync(string project, string path, CancellationToken c) => Record(c, "AddFileToProject", project, path);

    /// <summary>
    /// Asynchronously removes a specified file from the designated project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The path.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RemoveFileFromProjectAsync(string project, string path, CancellationToken c) => Record(c, "RemoveFileFromProject", project, path);

    /// <summary>
    /// Asynchronously retrieves the current debug state of the system.
    /// </summary>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the debug state.</returns>
    public Task<DebugState> GetDebugStateAsync(CancellationToken c) => Respond(c, State, "GetDebugState");

    /// <summary>
    /// Asynchronously processes a debug action and returns the resulting debug state.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the debug state.</returns>
    public Task<DebugState> DebugAsync(DebugAction action, CancellationToken c) => Respond(c, State, "Debug", action);

    /// <summary>
    /// Asynchronously retrieves a read-only list of all configured breakpoint information.
    /// </summary>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(CancellationToken c) => Respond<IReadOnlyList<BreakpointInfo>>(c, Breakpoints, "ListBreakpoints");

    /// <summary>
    /// Asynchronously records a request to add a breakpoint at the specified file path and line number, optionally including a conditional expression.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="condition">The condition.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task AddBreakpointAsync(string path, int line, string? condition, CancellationToken c) => Record(c, "AddBreakpoint", path, line, condition);

    /// <summary>
    /// Asynchronously removes breakpoints from the specified file path and optional line number.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the int.</returns>
    public Task<int> RemoveBreakpointsAsync(string path, int? line, CancellationToken c) => Respond(c, RemovedBreakpoints, "RemoveBreakpoints", path, line);

    /// <summary>
    /// Asynchronously evaluates the specified expression and returns the resulting value as a string.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string.</returns>
    public Task<string> EvaluateAsync(string expression, CancellationToken c) => Respond(c, Evaluation, "Evaluate", expression);

    /// <summary>
    /// Asynchronously retrieves the current call stack as a read-only list of strings.
    /// </summary>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<string>> GetCallStackAsync(CancellationToken c) => Respond<IReadOnlyList<string>>(c, CallStack, "GetCallStack");

    /// <summary>
    /// Asynchronously retrieves a read-only list of local variables.
    /// </summary>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<LocalVariable>> GetLocalsAsync(CancellationToken c) => Respond<IReadOnlyList<LocalVariable>>(c, Locals, "GetLocals");

    /// <summary>
    /// Asynchronously retrieves a filtered list of available commands, limited to the specified maximum number of results.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="max">The max.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<string>> FindCommandsAsync(string filter, int max, CancellationToken c) => Respond<IReadOnlyList<string>>(c, Commands, "FindCommands", filter, max);

    /// <summary>
    /// Asynchronously records the execution of a specified command and its associated arguments.
    /// </summary>
    /// <param name="command">The command containing the operation data.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="c">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task ExecuteCommandAsync(string command, string? arguments, CancellationToken c) => Record(c, "ExecuteCommand", command, arguments);
}
