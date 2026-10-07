using Newtonsoft.Json.Linq;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// The <c>vs_*</c> tools through which OMP's agent drives Visual Studio: validates arguments, keeps every path
/// inside the workspace, calls <see cref="IVsAutomation"/> and words the result for the model.
/// </summary>
internal sealed class VsHostTools : IHostTools
{
    private const int DefaultErrors = 100;
    private const int DefaultOutputLines = 200;
    private const int MaxOutputLines = 2000;
    private const int DefaultCommands = 50;

    private delegate Task<string> Handler(VsToolArguments arguments, CancellationToken cancellationToken);

    private readonly IVsAutomation _vs;
    private readonly WorkspaceScope _scope;
    private readonly Dictionary<string, Handler> _handlers;

    public VsHostTools(IVsAutomation automation, WorkspaceScope scope)
    {
        _vs = automation;
        _scope = scope;
        _handlers = new Dictionary<string, Handler>(StringComparer.Ordinal)
        {
            ["vs_solution"] = SolutionAsync,
            ["vs_documents"] = DocumentsAsync,
            ["vs_open_document"] = OpenDocumentAsync,
            ["vs_read_document"] = ReadDocumentAsync,
            ["vs_replace_lines"] = ReplaceLinesAsync,
            ["vs_save"] = SaveAsync,
            ["vs_close_document"] = CloseDocumentAsync,
            ["vs_selection"] = SelectionAsync,
            ["vs_build"] = BuildAsync,
            ["vs_errors"] = ErrorsAsync,
            ["vs_output"] = OutputAsync,
            ["vs_add_file_to_project"] = AddFileToProjectAsync,
            ["vs_remove_file_from_project"] = RemoveFileFromProjectAsync,
            ["vs_debug"] = DebugAsync,
            ["vs_breakpoints"] = BreakpointsAsync,
            ["vs_debug_inspect"] = DebugInspectAsync,
            ["vs_find_commands"] = FindCommandsAsync,
            ["vs_execute_command"] = ExecuteCommandAsync,
        };
    }

    public IReadOnlyList<HostToolDefinition> Definitions => VsToolDefinitions.All;

    public async Task<HostToolResult> InvokeAsync(string name, JObject arguments, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            throw new ArgumentException($"Unknown tool: {name}");
        }

        var text = await handler(new VsToolArguments(arguments, _scope), cancellationToken).ConfigureAwait(false);

        return HostToolResult.Text(VsToolFormat.Truncate(text));
    }

    private async Task<string> SolutionAsync(VsToolArguments a, CancellationToken ct) =>
        VsToolFormat.Solution(await _vs.GetSolutionAsync(ct).ConfigureAwait(false));

    private async Task<string> DocumentsAsync(VsToolArguments a, CancellationToken ct) =>
        VsToolFormat.Documents(await _vs.ListDocumentsAsync(ct).ConfigureAwait(false));

    private async Task<string> OpenDocumentAsync(VsToolArguments a, CancellationToken ct)
    {
        var path = a.RequiredPath("path");
        var line = a.OptionalInt("line", 1);
        var column = a.OptionalInt("column", 1);
        await _vs.OpenDocumentAsync(path, line, column, ct).ConfigureAwait(false);

        return VsToolFormat.Opened(path, line, column);
    }

    private async Task<string> ReadDocumentAsync(VsToolArguments a, CancellationToken ct)
    {
        var path = a.RequiredPath("path");
        var startLine = a.OptionalInt("startLine", 1);
        var endLine = a.OptionalInt("endLine", 1);
        if (startLine is not null && endLine is not null && endLine < startLine)
        {
            throw new ArgumentException("'endLine' must not be before 'startLine'");
        }

        return VsToolFormat.Document(await _vs.ReadDocumentAsync(path, startLine, endLine, ct).ConfigureAwait(false));
    }

    private async Task<string> ReplaceLinesAsync(VsToolArguments a, CancellationToken ct)
    {
        var path = a.RequiredPath("path");
        var startLine = a.RequiredInt("startLine", 1);
        var endLine = a.RequiredInt("endLine", 0);
        var text = a.RequiredString("text", allowEmpty: true);
        if (endLine < startLine - 1)
        {
            throw new ArgumentException("'endLine' must be at least startLine - 1");
        }

        await _vs.ReplaceLinesAsync(path, startLine, endLine, text, ct).ConfigureAwait(false);

        return VsToolFormat.Replaced(path, startLine, endLine);
    }

    private async Task<string> SaveAsync(VsToolArguments a, CancellationToken ct) =>
        VsToolFormat.Saved(await _vs.SaveDocumentsAsync(a.OptionalPath("path"), ct).ConfigureAwait(false));

    private async Task<string> CloseDocumentAsync(VsToolArguments a, CancellationToken ct)
    {
        var path = a.RequiredPath("path");
        var save = a.OptionalBool("save") ?? false;
        await _vs.CloseDocumentAsync(path, save, ct).ConfigureAwait(false);

        return save ? $"Saved and closed {path}." : $"Closed {path}.";
    }

    private async Task<string> SelectionAsync(VsToolArguments a, CancellationToken ct) =>
        VsToolFormat.Selection(await _vs.GetSelectionAsync(ct).ConfigureAwait(false));

    private async Task<string> BuildAsync(VsToolArguments a, CancellationToken ct)
    {
        var name = a.OptionalChoice("action", VsToolDefinitions.BuildActions, "build");
        var project = a.OptionalString("project");
        var configuration = a.OptionalString("configuration");
        var action = name == "rebuild" ? BuildAction.Rebuild : name == "clean" ? BuildAction.Clean : BuildAction.Build;

        return VsToolFormat.Build(action, await _vs.BuildAsync(action, project, configuration, ct).ConfigureAwait(false));
    }

    private async Task<string> ErrorsAsync(VsToolArguments a, CancellationToken ct)
    {
        var severity = a.OptionalChoice("severity", VsToolDefinitions.Severities, "error");
        var max = a.OptionalInt("max", 1) ?? DefaultErrors;

        return VsToolFormat.Errors(await _vs.GetErrorsAsync(ct).ConfigureAwait(false), severity, max);
    }

    private async Task<string> OutputAsync(VsToolArguments a, CancellationToken ct)
    {
        var pane = a.OptionalString("pane");
        var maxLines = Math.Min(a.OptionalInt("maxLines", 1) ?? DefaultOutputLines, MaxOutputLines);

        return VsToolFormat.Output(await _vs.ReadOutputAsync(pane, maxLines, ct).ConfigureAwait(false));
    }

    private async Task<string> AddFileToProjectAsync(VsToolArguments a, CancellationToken ct)
    {
        var project = a.RequiredString("project");
        var path = a.RequiredPath("path");
        await _vs.AddFileToProjectAsync(project, path, ct).ConfigureAwait(false);

        return $"Added {path} to project {project}.";
    }

    private async Task<string> RemoveFileFromProjectAsync(VsToolArguments a, CancellationToken ct)
    {
        var project = a.RequiredString("project");
        var path = a.RequiredPath("path");
        await _vs.RemoveFileFromProjectAsync(project, path, ct).ConfigureAwait(false);

        return $"Removed {path} from project {project}.";
    }

    private async Task<string> DebugAsync(VsToolArguments a, CancellationToken ct)
    {
        var action = a.RequiredChoice("action", VsToolDefinitions.DebugActions);
        DebugState state;
        switch (action)
        {
            case "start": state = await _vs.DebugAsync(DebugAction.Start, ct).ConfigureAwait(false); break;
            case "start_without_debugging": state = await _vs.DebugAsync(DebugAction.StartWithoutDebugging, ct).ConfigureAwait(false); break;
            case "stop": state = await _vs.DebugAsync(DebugAction.Stop, ct).ConfigureAwait(false); break;
            case "break": state = await _vs.DebugAsync(DebugAction.BreakAll, ct).ConfigureAwait(false); break;
            case "continue": state = await _vs.DebugAsync(DebugAction.Continue, ct).ConfigureAwait(false); break;
            case "step_into": state = await _vs.DebugAsync(DebugAction.StepInto, ct).ConfigureAwait(false); break;
            case "step_over": state = await _vs.DebugAsync(DebugAction.StepOver, ct).ConfigureAwait(false); break;
            case "step_out": state = await _vs.DebugAsync(DebugAction.StepOut, ct).ConfigureAwait(false); break;
            default: state = await _vs.GetDebugStateAsync(ct).ConfigureAwait(false); break;
        }

        return VsToolFormat.Debugger(state);
    }

    private async Task<string> BreakpointsAsync(VsToolArguments a, CancellationToken ct)
    {
        var action = a.RequiredChoice("action", VsToolDefinitions.BreakpointActions);
        if (action == "list")
        {
            return VsToolFormat.Breakpoints(await _vs.ListBreakpointsAsync(ct).ConfigureAwait(false));
        }

        var path = a.RequiredPath("path");
        if (action == "add")
        {
            var line = a.RequiredInt("line", 1);
            await _vs.AddBreakpointAsync(path, line, a.OptionalString("condition"), ct).ConfigureAwait(false);

            return $"Breakpoint set at {path}:{line}.";
        }

        var removeLine = a.OptionalInt("line", 1);
        var removed = await _vs.RemoveBreakpointsAsync(path, removeLine, ct).ConfigureAwait(false);

        return VsToolFormat.BreakpointsRemoved(removed, path, removeLine);
    }

    private async Task<string> DebugInspectAsync(VsToolArguments a, CancellationToken ct)
    {
        var kind = a.RequiredChoice("kind", VsToolDefinitions.InspectKinds);
        switch (kind)
        {
            case "evaluate":
                var expression = a.RequiredString("expression");
                return VsToolFormat.Evaluation(await _vs.EvaluateAsync(expression, ct).ConfigureAwait(false));

            case "callstack":
                return VsToolFormat.CallStack(await _vs.GetCallStackAsync(ct).ConfigureAwait(false));

            default:
                return VsToolFormat.Locals(await _vs.GetLocalsAsync(ct).ConfigureAwait(false));
        }
    }

    private async Task<string> FindCommandsAsync(VsToolArguments a, CancellationToken ct)
    {
        var filter = a.RequiredString("filter");
        var max = a.OptionalInt("max", 1) ?? DefaultCommands;

        return VsToolFormat.Commands(await _vs.FindCommandsAsync(filter, max, ct).ConfigureAwait(false), filter);
    }

    private async Task<string> ExecuteCommandAsync(VsToolArguments a, CancellationToken ct)
    {
        var command = a.RequiredString("command");
        var arguments = a.OptionalString("arguments");
        await _vs.ExecuteCommandAsync(command, arguments, ct).ConfigureAwait(false);

        return VsToolFormat.Executed(command, arguments);
    }
}
