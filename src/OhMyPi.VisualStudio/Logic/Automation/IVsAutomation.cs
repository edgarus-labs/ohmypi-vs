using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// What the agent can do in Visual Studio. The implementation (DTE and shell services) lives in the
/// package; the tool layer (<see cref="VsHostTools"/>) validates arguments and formats results on top of it.
/// Paths are absolute. Lines and columns are 1-based. Every method may be called from any thread.
/// A failure is an exception with a message the agent can act on.
/// </summary>
internal interface IVsAutomation
{
    Task<SolutionInfo> GetSolutionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentInfo>> ListDocumentsAsync(CancellationToken cancellationToken);

    /// <summary>Opens (or activates) the file and optionally moves the caret.</summary>
    Task OpenDocumentAsync(string path, int? line, int? column, CancellationToken cancellationToken);

    /// <summary>The text of the open editor buffer (unsaved edits included) or, when not open, of the file on disk. Lines are inclusive; null means the whole document.</summary>
    Task<DocumentText> ReadDocumentAsync(string path, int? startLine, int? endLine, CancellationToken cancellationToken);

    /// <summary>Replaces lines <paramref name="startLine"/>..<paramref name="endLine"/> (inclusive; endLine = startLine - 1 inserts before startLine) of the editor buffer, opening the file when needed. The document stays unsaved.</summary>
    Task ReplaceLinesAsync(string path, int startLine, int endLine, string text, CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="path"/>, or every dirty document when null. Returns the saved paths.</summary>
    Task<IReadOnlyList<string>> SaveDocumentsAsync(string? path, CancellationToken cancellationToken);

    Task CloseDocumentAsync(string path, bool save, CancellationToken cancellationToken);

    Task<SelectionInfo?> GetSelectionAsync(CancellationToken cancellationToken);

    /// <summary>Builds and waits for the end; <paramref name="project"/> null builds the solution.</summary>
    Task<BuildResult> BuildAsync(BuildAction action, string? project, string? configuration, CancellationToken cancellationToken);

    Task<IReadOnlyList<ErrorItem>> GetErrorsAsync(CancellationToken cancellationToken);

    /// <summary>The tail of an Output window pane (null name: the active pane); at most <paramref name="maxLines"/> lines.</summary>
    Task<string> ReadOutputAsync(string? pane, int maxLines, CancellationToken cancellationToken);

    Task AddFileToProjectAsync(string project, string path, CancellationToken cancellationToken);

    Task RemoveFileFromProjectAsync(string project, string path, CancellationToken cancellationToken);

    Task<DebugState> GetDebugStateAsync(CancellationToken cancellationToken);

    /// <summary>Runs the action; stepping and continuing wait until the debugger breaks or ends. Returns the state afterwards.</summary>
    Task<DebugState> DebugAsync(DebugAction action, CancellationToken cancellationToken);

    Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(CancellationToken cancellationToken);

    Task AddBreakpointAsync(string path, int line, string? condition, CancellationToken cancellationToken);

    /// <summary>Removes the breakpoints at <paramref name="path"/>:<paramref name="line"/> (every one when line is null). Returns how many were removed.</summary>
    Task<int> RemoveBreakpointsAsync(string path, int? line, CancellationToken cancellationToken);

    /// <summary>Evaluates in the current stack frame; the debugger must be in break mode.</summary>
    Task<string> EvaluateAsync(string expression, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetCallStackAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalVariable>> GetLocalsAsync(CancellationToken cancellationToken);

    /// <summary>Command names (<c>Edit.FormatDocument</c>, …) containing <paramref name="filter"/>, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<string>> FindCommandsAsync(string filter, int max, CancellationToken cancellationToken);

    Task ExecuteCommandAsync(string command, string? arguments, CancellationToken cancellationToken);
}
