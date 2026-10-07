using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>Turns automation results into the compact text the agent reads.</summary>
internal static class VsToolFormat
{
    public const int MaxResultChars = 40000;
    private const int MarkerReserve = 200;
    public const int MaxBuildDiagnostics = 50;

    public static string Solution(SolutionInfo solution)
    {
        if (solution.Path is null)
        {
            return "No solution is open.";
        }

        var lines = new List<string> { $"Solution: {solution.Path}" };
        if (solution.ActiveConfiguration is not null)
        {
            lines.Add($"Configuration: {solution.ActiveConfiguration}" + (solution.ActivePlatform is not null ? "|" + solution.ActivePlatform : ""));
        }

        if (solution.StartupProject is not null)
        {
            lines.Add($"Startup project: {solution.StartupProject}");
        }

        if (solution.ActiveDocument is not null)
        {
            lines.Add($"Active document: {solution.ActiveDocument}");
        }

        lines.Add($"Projects ({solution.Projects.Count}):");
        foreach (var project in solution.Projects)
        {
            lines.Add($"- {project.Name}" + (project.Path is not null ? ": " + project.Path : "") + (project.Kind is not null ? $" ({project.Kind})" : ""));
        }

        return string.Join("\n", lines);
    }

    public static string Documents(IReadOnlyList<DocumentInfo> documents)
    {
        if (documents.Count == 0)
        {
            return "No documents are open.";
        }

        return string.Join("\n", documents.Select(d =>
            d.Path + (d.IsActive ? " [active]" : "") + (d.IsDirty ? " [unsaved]" : "") + (d.IsReadOnly ? " [read-only]" : "")));
    }

    public static string Opened(string path, int? line, int? column)
    {
        var where = line is null ? "" : column is null ? $" at line {line}" : $" at line {line}, column {column}";

        return $"Opened {path}{where}.";
    }

    public static string Document(DocumentText document)
    {
        var header = $"{document.Path} (lines {document.StartLine}-{document.EndLine} of {document.TotalLines}, {(document.FromEditor ? "editor buffer" : "disk")})";
        var count = document.EndLine - document.StartLine + 1;
        if (count <= 0)
        {
            return header + "\n(no lines)";
        }

        var lines = document.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > count && lines[lines.Count - 1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var width = document.EndLine.ToString().Length;
        var text = new StringBuilder(header);
        for (var i = 0; i < lines.Count; i++)
        {
            text.Append('\n').Append((document.StartLine + i).ToString().PadLeft(width)).Append(": ").Append(lines[i]);
        }

        return Truncate(text.ToString(), "; narrow the range with startLine/endLine");
    }

    public static string Replaced(string path, int startLine, int endLine)
    {
        var what = endLine < startLine ? $"Inserted text before line {startLine}" : $"Replaced lines {startLine}-{endLine}";

        return $"{what} of {path}. The document has unsaved changes; call vs_save to write it.";
    }

    public static string Saved(IReadOnlyList<string> paths) =>
        paths.Count == 0 ? "Nothing to save." : $"Saved {paths.Count} document(s):\n" + string.Join("\n", paths);

    public static string Selection(SelectionInfo? selection)
    {
        if (selection is null)
        {
            return "No active text editor.";
        }

        if (selection.Text.Length == 0)
        {
            return $"{selection.Path} caret at {selection.StartLine}:{selection.StartColumn} (nothing selected)";
        }

        return $"{selection.Path} {selection.StartLine}:{selection.StartColumn}-{selection.EndLine}:{selection.EndColumn}\n{selection.Text}";
    }

    public static string Build(BuildAction action, BuildResult result)
    {
        var errors = result.Errors.Where(e => IsSeverity(e, "error")).ToList();
        var warnings = result.Errors.Where(e => IsSeverity(e, "warning")).ToList();
        var lines = new List<string>
        {
            $"{action} {(result.Succeeded ? "succeeded" : "failed")}: {result.ProjectsSucceeded} project(s) succeeded, {result.ProjectsFailed} failed, {result.ProjectsSkipped} skipped.",
            $"Errors: {errors.Count}, warnings: {warnings.Count}.",
        };
        var diagnostics = errors.Concat(warnings).ToList();
        lines.AddRange(diagnostics.Take(MaxBuildDiagnostics).Select(Diagnostic));
        if (diagnostics.Count > MaxBuildDiagnostics)
        {
            lines.Add($"… {diagnostics.Count - MaxBuildDiagnostics} more; use vs_errors to list them.");
        }

        return string.Join("\n", lines);
    }

    public static string Errors(IReadOnlyList<ErrorItem> items, string severity, int max)
    {
        var label = severity == "all" ? "item" : severity;
        var matching = severity == "all" ? items.ToList() : [.. items.Where(e => IsSeverity(e, severity))];
        if (matching.Count == 0)
        {
            return $"No {label}s.";
        }

        var header = matching.Count > max
            ? $"Showing first {max} of {matching.Count} {label}(s):"
            : $"{matching.Count} {label}(s):";

        return header + "\n" + string.Join("\n", matching.Take(max).Select(Diagnostic));
    }

    public static string Output(string output) =>
        output.Trim().Length == 0 ? "The output pane is empty." : TruncateTail(output);

    public static string Debugger(DebugState state)
    {
        var text = $"Debugger mode: {state.Mode}";
        if (state.Location is not null)
        {
            text += $"\nLocation: {state.Location}";
        }

        if (state.Exception is not null)
        {
            text += $"\nException: {state.Exception}";
        }

        return text;
    }

    public static string Breakpoints(IReadOnlyList<BreakpointInfo> breakpoints) =>
        breakpoints.Count == 0
            ? "No breakpoints."
            : string.Join("\n", breakpoints.Select(b =>
                $"{b.Path}:{b.Line}" + (b.Condition != null ? $" [condition: {b.Condition}]" : "") + (b.Enabled ? "" : " [disabled]")));

    public static string BreakpointsRemoved(int removed, string path, int? line) =>
        line is null ? $"Removed {removed} breakpoint(s) in {path}." : $"Removed {removed} breakpoint(s) at {path}:{line}.";

    public static string Evaluation(string value) => value.Length == 0 ? "(empty result)" : value;

    public static string CallStack(IReadOnlyList<string> frames) =>
        frames.Count == 0 ? "No stack frames." : string.Join("\n", frames.Select((f, i) => $"{i}: {f}"));

    public static string Locals(IReadOnlyList<LocalVariable> locals) =>
        locals.Count == 0
            ? "No local variables."
            : string.Join("\n", locals.Select(v => v.Name + (v.Type != null ? $" ({v.Type})" : "") + (v.Value != null ? $" = {v.Value}" : "")));

    public static string Commands(IReadOnlyList<string> commands, string filter) =>
        commands.Count == 0 ? $"No commands match '{filter}'." : string.Join("\n", commands);

    public static string Executed(string command, string? arguments) =>
        arguments is null ? $"Executed {command}." : $"Executed {command} with arguments: {arguments}.";

    /// <summary>Shortens <paramref name="text"/> to <see cref="MaxResultChars"/> including the marker, keeping the start and cutting at a line break when one is near.</summary>
    public static string Truncate(string text, string hint = "")
    {
        if (text.Length <= MaxResultChars)
        {
            return text;
        }

        var keep = MaxResultChars - MarkerReserve;
        var cut = text.LastIndexOf('\n', keep - 1);
        if (cut < keep / 2)
        {
            cut = keep;
        }

        return text.Substring(0, cut) + $"\n… [truncated: {text.Length - cut} more characters{hint}]";
    }

    /// <summary>Shortens <paramref name="text"/> to <see cref="MaxResultChars"/> including the marker, keeping the end.</summary>
    private static string TruncateTail(string text)
    {
        if (text.Length <= MaxResultChars)
        {
            return text;
        }

        var cut = text.Length - (MaxResultChars - MarkerReserve);

        return $"… [truncated: {cut} earlier characters]\n" + text.Substring(cut);
    }

    private static bool IsSeverity(ErrorItem item, string severity) =>
        string.Equals(item.Severity, severity, StringComparison.OrdinalIgnoreCase);

    private static string Diagnostic(ErrorItem item)
    {
        var text = new StringBuilder(item.Severity);
        if (!string.IsNullOrEmpty(item.Code))
        {
            text.Append(' ').Append(item.Code);
        }

        if (!string.IsNullOrEmpty(item.Path))
        {
            text.Append(' ').Append(item.Path);
            if (item.Line > 0)
            {
                text.Append(item.Column > 0 ? $"({item.Line},{item.Column})" : $"({item.Line})");
            }
        }
        text.Append(": ").Append(item.Message);
        if (!string.IsNullOrEmpty(item.Project))
        {
            text.Append(" [").Append(item.Project).Append(']');
        }

        return text.ToString();
    }
}
