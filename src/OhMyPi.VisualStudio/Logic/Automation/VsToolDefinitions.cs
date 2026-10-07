using Newtonsoft.Json.Linq;
using Omp.Core;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>The Visual Studio tools offered to the agent: names, guidance for the model and argument schemas.</summary>
internal static class VsToolDefinitions
{
    public static readonly string[] BuildActions = ["build", "rebuild", "clean"];
    public static readonly string[] Severities = ["error", "warning", "message", "all"];
    public static readonly string[] DebugActions = ["start", "start_without_debugging", "stop", "break", "continue", "step_into", "step_over", "step_out", "state"];
    public static readonly string[] BreakpointActions = ["list", "add", "remove"];
    public static readonly string[] InspectKinds = ["evaluate", "callstack", "locals"];

    public static IReadOnlyList<HostToolDefinition> All { get; } = Create();

    private static IReadOnlyList<HostToolDefinition> Create()
    {
        const string pathNote = "Relative paths are resolved against the working directory; the file must be inside the workspace.";

        return new[]
        {
            Tool("vs_solution",
                "Describe the solution open in Visual Studio: path, active configuration and platform, startup project, active document and all projects. Use it first to learn what the IDE has loaded and to get exact project names for the other vs_ tools.",
                new JObject()),
            Tool("vs_documents",
                "List the documents open in Visual Studio editor tabs with their state (active, unsaved changes, read-only). Use it to see what the user is looking at or which files carry unsaved edits.",
                new JObject()),
            Tool("vs_open_document",
                "Open a file in a Visual Studio editor tab (or activate it) and optionally move the caret there. Use it to show the user a file or a location, for example after finding a bug or an error. " + pathNote,
                new JObject
                {
                    ["path"] = Str("File to open."),
                    ["line"] = Int("1-based line to place the caret on.", 1),
                    ["column"] = Int("1-based column to place the caret on.", 1),
                },
                "path"),
            Tool("vs_read_document",
                "Read a file with line numbers through Visual Studio: the editor buffer including unsaved edits when the file is open, otherwise the file on disk. Use it instead of reading from disk when the user may have unsaved changes, and to get the line numbers vs_replace_lines needs. " + pathNote,
                new JObject
                {
                    ["path"] = Str("File to read."),
                    ["startLine"] = Int("First line to read (1-based). Defaults to the first line.", 1),
                    ["endLine"] = Int("Last line to read (inclusive). Defaults to the last line.", 1),
                },
                "path"),
            Tool("vs_replace_lines",
                "Replace a range of lines of a file in the Visual Studio editor buffer, which keeps the editor's undo history and shows the edit live. The document is left unsaved; call vs_save to write it. Use it to edit a file the user has open. To insert before a line set endLine to startLine - 1; to delete lines pass empty text. " + pathNote,
                new JObject
                {
                    ["path"] = Str("File to edit."),
                    ["startLine"] = Int("First line to replace (1-based). May be one past the last line to append.", 1),
                    ["endLine"] = Int("Last line to replace (inclusive). startLine - 1 inserts before startLine.", 0),
                    ["text"] = Str("Replacement text; empty deletes the lines."),
                },
                "path", "startLine", "endLine", "text"),
            Tool("vs_save",
                "Save one document, or every document with unsaved changes when no path is given. Use it after vs_replace_lines, before building or running so the compiler sees the edits. " + pathNote,
                new JObject { ["path"] = Str("File to save. Omit to save all unsaved documents.") }),
            Tool("vs_close_document",
                "Close a document tab in Visual Studio. Unsaved changes are discarded unless save is true. " + pathNote,
                new JObject
                {
                    ["path"] = Str("File whose tab to close."),
                    ["save"] = Bool("Save the document before closing. Defaults to false (unsaved changes are discarded)."),
                },
                "path"),
            Tool("vs_selection",
                "Get the active editor's selection (or caret position) and the selected text. Use it when the user refers to \"this\", \"the selected code\" or \"here\".",
                new JObject()),
            Tool("vs_build",
                "Build, rebuild or clean the solution or one project with Visual Studio's own build system and wait for it to finish. Returns per-project counts and up to 50 errors and warnings. Use it to verify a change compiles; call vs_save first so unsaved edits are included.",
                new JObject
                {
                    ["action"] = Choice("What to run. Defaults to build.", BuildActions),
                    ["project"] = Str("Project name (see vs_solution). Omit to act on the whole solution."),
                    ["configuration"] = Str("Solution configuration such as Debug or Release. Omit to use the active one."),
                }),
            Tool("vs_errors",
                "List the diagnostics currently shown in Visual Studio's Error List, including live analysis of open files. Use it to see what is wrong without running a build.",
                new JObject
                {
                    ["severity"] = Choice("Which diagnostics to list. Defaults to error.", Severities),
                    ["max"] = Int("Maximum number of entries. Defaults to 100.", 1),
                }),
            Tool("vs_output",
                "Read the tail of a Visual Studio Output window pane (Build, Debug, Tests, ...). Use it for build logs, debugger output and test results.",
                new JObject
                {
                    ["pane"] = Str("Pane name, for example Build or Debug. Omit for the active pane."),
                    ["maxLines"] = Int("Number of trailing lines to return. Defaults to 200, at most 2000.", 1),
                }),
            Tool("vs_add_file_to_project",
                "Add an existing file on disk to a Visual Studio project so it is compiled or shipped with it. Use it after creating a file for a project that does not include files by glob. " + pathNote,
                new JObject
                {
                    ["project"] = Str("Project name (see vs_solution)."),
                    ["path"] = Str("File to add."),
                },
                "project", "path"),
            Tool("vs_remove_file_from_project",
                "Remove a file from a Visual Studio project without deleting it from disk. " + pathNote,
                new JObject
                {
                    ["project"] = Str("Project name (see vs_solution)."),
                    ["path"] = Str("File to remove."),
                },
                "project", "path"),
            Tool("vs_debug",
                "Control the Visual Studio debugger: start (F5), start without debugging (Ctrl+F5), stop, break all, continue and step. Stepping and continuing wait until the debugger breaks again or ends and report where it stopped. Use action state to see the current mode and location.",
                new JObject { ["action"] = Choice("Debugger operation.", DebugActions) },
                "action"),
            Tool("vs_breakpoints",
                "List, add or remove breakpoints in Visual Studio. Use it to prepare a debugging session before vs_debug start. " + pathNote,
                new JObject
                {
                    ["action"] = Choice("list shows all breakpoints, add sets one, remove deletes the ones at a location.", BreakpointActions),
                    ["path"] = Str("File of the breakpoint. Required for add and remove."),
                    ["line"] = Int("1-based line. Required for add; remove deletes every breakpoint in the file when omitted.", 1),
                    ["condition"] = Str("Condition expression for add; the breakpoint only hits when it is true."),
                },
                "action"),
            Tool("vs_debug_inspect",
                "Inspect the program stopped in the Visual Studio debugger: evaluate an expression in the current frame, show the call stack or the local variables. The debugger must be in break mode (see vs_debug).",
                new JObject
                {
                    ["kind"] = Choice("What to inspect.", InspectKinds),
                    ["expression"] = Str("Expression to evaluate. Required for kind evaluate."),
                },
                "kind"),
            Tool("vs_find_commands",
                "Search Visual Studio command names (such as Edit.FormatDocument or Build.BuildSolution) by substring. Use it to discover the exact name before vs_execute_command.",
                new JObject
                {
                    ["filter"] = Str("Case-insensitive text the command name must contain."),
                    ["max"] = Int("Maximum number of commands. Defaults to 50.", 1),
                },
                "filter"),
            Tool("vs_execute_command",
                "Run any Visual Studio command by name, as if chosen from the menu, for IDE features the other vs_ tools do not cover (format document, organize usings, refactorings, ...). Find the name with vs_find_commands. The command acts on the active document.",
                new JObject
                {
                    ["command"] = Str("Command name, for example Edit.FormatDocument."),
                    ["arguments"] = Str("Command arguments, as typed after the command name in the Command Window."),
                },
                "command"),
        };
    }

    private static HostToolDefinition Tool(string name, string description, JObject properties, params string[] required) =>
        new HostToolDefinition(name, description, new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray(required),
            ["additionalProperties"] = false,
        });

    private static JObject Str(string description) => new JObject { ["type"] = "string", ["description"] = description };

    private static JObject Int(string description, int minimum) =>
        new JObject { ["type"] = "integer", ["description"] = description, ["minimum"] = minimum };

    private static JObject Bool(string description) => new JObject { ["type"] = "boolean", ["description"] = description };

    private static JObject Choice(string description, string[] values) =>
        new JObject { ["type"] = "string", ["description"] = description, ["enum"] = new JArray(values) };
}
