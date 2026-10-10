using EnvDTE;
using Microsoft.VisualStudio.Shell;
using OhMyPi.VisualStudio.Logic.Automation;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTE2 = EnvDTE80.DTE2;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio;

internal sealed partial class VsAutomation
{
    private const int MaxStackFrames = 100;
    private const int MaxLocals = 500;
    private const int ExpressionTimeoutMilliseconds = 10000;
    private static readonly TimeSpan ResumeWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DebugStartGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LaunchAfterBuildGrace = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ModeChangeWait = TimeSpan.FromSeconds(30);

    public async Task<DebugState> GetDebugStateAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);

        return ReadDebugState(dte);
    }

    public async Task<DebugState> DebugAsync(DebugAction action, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var debugger = dte.Debugger;
        switch (action)
        {
            case DebugAction.Start:
                return await StartAsync(dte, debugWith: true, cancellationToken);

            case DebugAction.StartWithoutDebugging:
                return await StartAsync(dte, debugWith: false, cancellationToken);

            case DebugAction.Stop:
                if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
                {
                    return ReadDebugState(dte);
                }

                debugger.Stop(false);
                await WaitForModeAsync(debugger, dbgDebugMode.dbgDesignMode, ModeChangeWait, cancellationToken);
                return ReadDebugState(dte);

            case DebugAction.BreakAll:
                if (debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
                {
                    throw new InvalidOperationException("The debugger is not running; there is nothing to break into.");
                }

                if (debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                {
                    return ReadDebugState(dte);
                }

                debugger.Break(false);
                await WaitForModeAsync(debugger, dbgDebugMode.dbgBreakMode, ModeChangeWait, cancellationToken);
                return ReadDebugState(dte);

            case DebugAction.Continue:
                return await ResumeAsync(dte, DebugAction.Continue, cancellationToken);

            case DebugAction.StepInto:
                return await ResumeAsync(dte, DebugAction.StepInto, cancellationToken);

            case DebugAction.StepOver:
                return await ResumeAsync(dte, DebugAction.StepOver, cancellationToken);

            case DebugAction.StepOut:
                return await ResumeAsync(dte, DebugAction.StepOut, cancellationToken);

            default:
                throw new InvalidOperationException($"Unsupported debugger action {action}.");
        }
    }

    private async Task<DebugState> StartAsync(DTE2 dte, bool debugWith, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var debugger = dte.Debugger;
        if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
        {
            throw new InvalidOperationException($"The debugger is already {ModeName(debugger.CurrentMode)}; stop it first.");
        }

        if (dte.Solution is null || !dte.Solution.IsOpen)
        {
            throw new InvalidOperationException("No solution is open.");
        }

        var build = dte.Solution.SolutionBuild;
        ExecuteNamedCommand(dte, debugWith ? "Debug.Start" : "Debug.StartWithoutDebugging", null);

        var started = DateTime.UtcNow;
        DateTime? idleSince = null;
        var sawBuild = false;
        using (var linked = LinkToDisposal(cancellationToken))
        {
            while (DateTime.UtcNow - started < StartWait)
            {
                await Task.Delay(PollInterval, linked.Token);
                if (debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
                {
                    break;
                }

                if (build.BuildState == vsBuildState.vsBuildStateInProgress)
                {
                    sawBuild = true;
                    idleSince = null;
                    continue;
                }
                idleSince = idleSince ?? DateTime.UtcNow;
                var grace = debugWith ? DebugStartGrace : sawBuild ? LaunchAfterBuildGrace : LaunchGrace;
                if (DateTime.UtcNow - idleSince.Value > grace)
                {
                    break;
                }
            }
        }

        return ReadDebugState(dte);
    }

    private async Task<DebugState> ResumeAsync(DTE2 dte, DebugAction step, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var debugger = dte.Debugger;
        RequireMode(debugger, dbgDebugMode.dbgBreakMode);

        var events = dte.Events.DebuggerEvents;
        var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispDebuggerEvents_OnEnterBreakModeEventHandler onBreak = (dbgEventReason reason, ref dbgExecutionAction action) => stopped.TrySetResult(true);
        _dispDebuggerEvents_OnEnterDesignModeEventHandler onDesign = reason => stopped.TrySetResult(true);
        events.OnEnterBreakMode += onBreak;
        events.OnEnterDesignMode += onDesign;
        try
        {
            try
            {
                Resume(debugger, step);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"The debugger could not continue: {ex.Message}", ex);
            }
#pragma warning disable VSTHRD003 // The task is completed by a DTE debugger event handler that runs on the UI thread this method resumes on.
            await WaitAsync(stopped.Task, ResumeWait, cancellationToken);
#pragma warning restore VSTHRD003
        }
        finally
        {
            events.OnEnterBreakMode -= onBreak;
            events.OnEnterDesignMode -= onDesign;
        }

        return ReadDebugState(dte);
    }

    /// <summary>
    /// Resumes debugger execution by performing the specified stepping action or continuing execution.
    /// </summary>
    /// <param name="debugger">The debugger.</param>
    /// <param name="step">The step.</param>
    private static void Resume(EnvDTE.Debugger debugger, DebugAction step)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        switch (step)
        {
            case DebugAction.StepInto: debugger.StepInto(false); break;
            case DebugAction.StepOver: debugger.StepOver(false); break;
            case DebugAction.StepOut: debugger.StepOut(false); break;
            default: debugger.Go(false); break;
        }
    }

    /// <summary>
    /// Asynchronously waits for the debugger to transition to the specified debug mode within the given timeout period.
    /// </summary>
    /// <param name="debugger">The debugger.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="timeout">The timeout.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task WaitForModeAsync(EnvDTE.Debugger debugger, dbgDebugMode mode, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var started = DateTime.UtcNow;
        using (var linked = LinkToDisposal(cancellationToken))
        {
            while (debugger.CurrentMode != mode && DateTime.UtcNow - started < timeout)
            {
                await Task.Delay(PollInterval, linked.Token);
            }
        }
    }

    /// <summary>
    /// Validates that the debugger is currently in the specified debug mode and throws an &lt;see cref=&quot;InvalidOperationException&quot;/&gt; if the requirement is not met.
    /// </summary>
    /// <param name="debugger">The debugger.</param>
    /// <param name="required">The required.</param>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static void RequireMode(EnvDTE.Debugger debugger, dbgDebugMode required)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var mode = debugger.CurrentMode;
        if (mode == required)
        {
            return;
        }

        if (required == dbgDebugMode.dbgBreakMode)
        {
            throw new InvalidOperationException(mode == dbgDebugMode.dbgRunMode
                ? "The debugger is not in break mode (the program is running). Use vs_debug with action break first."
                : "The debugger is not in break mode (no debugging session). Use vs_debug with action start first.");
        }

        throw new InvalidOperationException($"The debugger is {ModeName(mode)}, expected {ModeName(required)}.");
    }

    /// <summary>
    /// Converts a specified debug mode enumeration value into its corresponding string representation.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The string result.</returns>
    private static string ModeName(dbgDebugMode mode)
    {
        switch (mode)
        {
            case dbgDebugMode.dbgRunMode: return "run";
            case dbgDebugMode.dbgBreakMode: return "break";
            default: return "design";
        }
    }

    /// <summary>
    /// Retrieves the current debugger state, including the execution mode, stack frame location, and exception details if the debugger is currently in break mode.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <returns>The debug state result.</returns>
    private static DebugState ReadDebugState(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var debugger = dte.Debugger;
        var state = new DebugState { Mode = ModeName(debugger.CurrentMode) };
        if (debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
        {
            return state;
        }

        state.Location = DescribeFrame(debugger.CurrentStackFrame);
        var reason = debugger.LastBreakReason;
        if (reason == dbgEventReason.dbgEventReasonExceptionThrown || reason == dbgEventReason.dbgEventReasonExceptionNotHandled)
        {
            state.Exception = DescribeException(debugger);
        }

        return state;
    }

    /// <summary>
    /// Generates a human-readable description of a stack frame, including the function name, module, and source file location if available.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The string? result.</returns>
    private static string? DescribeFrame(StackFrame? frame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (frame is null)
        {
            return null;
        }

        try
        {
            var function = frame.FunctionName;
            if (frame is EnvDTE90a.StackFrame2 detailed && !string.IsNullOrEmpty(detailed.FileName))
            {
                return $"{function} at {detailed.FileName}:{detailed.LineNumber}";
            }

            var module = frame.Module;

            return string.IsNullOrEmpty(module) ? function : $"{function} [{module}]";
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves a formatted description of the current exception from the debugger, combining the exception type and message if available.
    /// </summary>
    /// <param name="debugger">The debugger.</param>
    /// <returns>The string? result.</returns>
    private static string? DescribeException(EnvDTE.Debugger debugger)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var exception = debugger.GetExpression("$exception", false, ExpressionTimeoutMilliseconds);
            if (!exception.IsValidValue)
            {
                return null;
            }

            var message = debugger.GetExpression("$exception.Message", false, ExpressionTimeoutMilliseconds);

            return message.IsValidValue ? $"{exception.Type}: {message.Value}" : $"{exception.Type}: {exception.Value}";
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asynchronously retrieves a list of all active debugger breakpoints, including their file paths, line numbers, conditions, and enabled states.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public async Task<IReadOnlyList<BreakpointInfo>> ListBreakpointsAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var breakpoints = new List<BreakpointInfo>();
        foreach (Breakpoint breakpoint in dte.Debugger.Breakpoints)
        {
            var file = breakpoint.File;
            if (string.IsNullOrEmpty(file))
            {
                continue;
            }

            breakpoints.Add(new BreakpointInfo
            {
                Path = file,
                Line = breakpoint.FileLine,
                Condition = NullIfEmpty(breakpoint.Condition),
                Enabled = breakpoint.Enabled,
            });
        }

        return breakpoints;
    }

    /// <summary>
    /// Asynchronously adds a debugger breakpoint at the specified file path and line number with an optional execution condition.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="condition">The condition.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task AddBreakpointAsync(string path, int line, string? condition, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var file = ExistingFile(path);
        try
        {
            dte.Debugger.Breakpoints.Add(File: file, Line: line, Condition: condition ?? "");
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not set a breakpoint at {file}:{line}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Asynchronously removes breakpoints from a specified file path, optionally filtering by line number, and returns the total number of breakpoints deleted.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="line">The line.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the int.</returns>
    public async Task<int> RemoveBreakpointsAsync(string path, int? line, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var file = FullPath(path);
        var matching = new List<Breakpoint>();
        foreach (Breakpoint breakpoint in dte.Debugger.Breakpoints)
        {
            if (SamePath(breakpoint.File, file) && (line is null || breakpoint.FileLine == line))
            {
                matching.Add(breakpoint);
            }
        }
        foreach (var breakpoint in matching)
        {
            breakpoint.Delete();
        }

        return matching.Count;
    }

    /// <summary>
    /// Asynchronously evaluates a debugger expression and returns its value and type as a formatted string.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<string> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        RequireMode(dte.Debugger, dbgDebugMode.dbgBreakMode);
        Expression result;
        try
        {
            result = dte.Debugger.GetExpression(expression, false, ExpressionTimeoutMilliseconds);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Cannot evaluate '{expression}': {ex.Message}", ex);
        }
        if (!result.IsValidValue)
        {
            throw new InvalidOperationException($"Cannot evaluate '{expression}': {result.Value}");
        }

        return string.IsNullOrEmpty(result.Type) ? result.Value : $"{result.Value} ({result.Type})";
    }

    /// <summary>
    /// Asynchronously retrieves a formatted list of call stack frames from the current debugger thread while the debugger is in break mode.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<IReadOnlyList<string>> GetCallStackAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        RequireMode(dte.Debugger, dbgDebugMode.dbgBreakMode);
        var frames = new List<string>();
        var thread = dte.Debugger.CurrentThread ?? throw new InvalidOperationException("The debugger has no current thread.");
        foreach (StackFrame frame in thread.StackFrames)
        {
            if (frames.Count == MaxStackFrames)
            {
                frames.Add($"... (more than {MaxStackFrames} frames)");
                break;
            }
            frames.Add($"#{frames.Count} {DescribeFrame(frame) ?? "(unavailable frame)"}");
        }

        return frames;
    }

    /// <summary>
    /// Asynchronously retrieves a list of local variables from the current debugger stack frame.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<IReadOnlyList<LocalVariable>> GetLocalsAsync(CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        RequireMode(dte.Debugger, dbgDebugMode.dbgBreakMode);
        var frame = dte.Debugger.CurrentStackFrame ?? throw new InvalidOperationException("The debugger has no current stack frame.");
        var locals = new List<LocalVariable>();
        foreach (Expression local in frame.Locals)
        {
            if (locals.Count == MaxLocals)
            {
                break;
            }

            locals.Add(new LocalVariable { Name = local.Name, Type = NullIfEmpty(local.Type), Value = local.IsValidValue ? local.Value : "<unavailable>" });
        }

        return locals;
    }
}
