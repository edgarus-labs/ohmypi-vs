using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using OhMyPi.VisualStudio.Logic.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTE2 = EnvDTE80.DTE2;
using ErrorItem = OhMyPi.VisualStudio.Logic.Automation.ErrorItem;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio;

internal sealed partial class VsAutomation
{
    private const int ErrorListSettleReads = 3;
    private const int ErrorListMaxReads = 24;
    private static readonly TimeSpan ErrorListReadInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Counts the projects a build step finished and signals the end of the step, from the DTE build events.</summary>
    private sealed class BuildWatcher : IDisposable
    {
        private readonly BuildEvents _events;
        private readonly _dispBuildEvents_OnBuildDoneEventHandler _onDone;
        private readonly _dispBuildEvents_OnBuildProjConfigDoneEventHandler _onProject;
        private TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public BuildWatcher(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _events = dte.Events.BuildEvents;
            _onDone = (scope, action) => _done.TrySetResult(true);
            _onProject = (project, projectConfig, platform, solutionConfig, success) =>
            {
                if (success)
                {
                    Succeeded++;
                }
                else
                {
                    Failed++;
                }
            };
            _events.OnBuildDone += _onDone;
            _events.OnBuildProjConfigDone += _onProject;
        }

        public int Succeeded { get; private set; }

        public int Failed { get; private set; }

        public Task Done => _done.Task;

        public void Reset()
        {
            Succeeded = 0;
            Failed = 0;
            _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _events.OnBuildDone -= _onDone;
            _events.OnBuildProjConfigDone -= _onProject;
        }
    }

    public async Task<BuildResult> BuildAsync(BuildAction action, string? project, string? configuration, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        if (dte.Solution is null || !dte.Solution.IsOpen)
        {
            throw new InvalidOperationException("No solution is open.");
        }

        var build = dte.Solution.SolutionBuild;
        if (build.BuildState == vsBuildState.vsBuildStateInProgress)
        {
            throw new InvalidOperationException("A build is already running.");
        }

        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode)
        {
            throw new InvalidOperationException("The debugger is running; stop debugging before building.");
        }

        var target = project is null ? null : FindProject(dte, project);
        var requested = configuration is null ? null : FindConfiguration(build, configuration);
        var previous = build.ActiveConfiguration;
        var switched = requested is not null && !SameConfiguration(requested, previous);

        var expected = target is null ? CountBuildableProjects(build) : 0;
        var result = new BuildResult();
        using (var linked = LinkToDisposal(cancellationToken))
        using (var watcher = new BuildWatcher(dte))
        {
            var failed = true;
            try
            {
                if (switched)
                {
                    requested!.Activate();
                }

                if (action == BuildAction.Clean || action == BuildAction.Rebuild)
                {
                    await RunBuildStepAsync(dte, build, watcher, () => StartClean(dte, build, target), linked.Token);
                    if (action == BuildAction.Rebuild)
                    {
                        watcher.Reset();
                    }
                }
                if (action == BuildAction.Build || action == BuildAction.Rebuild)
                {
                    await RunBuildStepAsync(dte, build, watcher, () => StartBuild(build, target), linked.Token);
                }

                failed = false;
            }
            catch (OperationCanceledException)
            {
                CancelRunningBuild(dte, build);

                throw;
            }
            finally
            {
                if (switched)
                {
                    RestoreConfiguration(previous, failed);
                }
            }

            result.ProjectsSucceeded = watcher.Succeeded;
            result.ProjectsFailed = Math.Max(watcher.Failed, build.LastBuildInfo);
            result.ProjectsSkipped = Math.Max(0, expected - watcher.Succeeded - watcher.Failed);
            result.Succeeded = result.ProjectsFailed == 0;
        }

        if (action != BuildAction.Clean)
        {
            var items = await ReadStableErrorListAsync(cancellationToken);
            result.Errors.AddRange(items.Where(item => item.Severity != "message"));
            if (result.Errors.Any(item => item.Severity == "error"))
            {
                result.Succeeded = false;
            }
        }

        return result;
    }

    private static void RestoreConfiguration(SolutionConfiguration previous, bool swallow)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            previous.Activate();
        }
        catch (COMException ex)
        {
            if (!swallow)
            {
                throw new InvalidOperationException($"Visual Studio could not restore the previous solution configuration: {ex.Message}", ex);
            }
        }
    }

    private async Task RunBuildStepAsync(DTE2 dte, SolutionBuild build, BuildWatcher watcher, Action start, CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        try
        {
            start();
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not start the build: {ex.Message}", ex);
        }

        var sawProgress = build.BuildState == vsBuildState.vsBuildStateInProgress;
#pragma warning disable VSTHRD003 // The task is completed by a DTE build event handler that runs on the UI thread this method resumes on.
        while (!await WaitAsync(watcher.Done, BuildPollInterval, cancellationToken))
#pragma warning restore VSTHRD003
        {
            var state = build.BuildState;
            if (state == vsBuildState.vsBuildStateInProgress)
            {
                sawProgress = true;
            }
            else if (state == vsBuildState.vsBuildStateDone && sawProgress)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Initiates the build process for either the entire solution or a specific target project.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="target">The target.</param>
    private void StartBuild(SolutionBuild build, Project? target)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (target is null)
        {
            build.Build(false);
        }
        else
        {
            build.BuildProject(build.ActiveConfiguration.Name, target.UniqueName, false);
        }
    }

    /// <summary>
    /// Initiates a clean operation for the entire solution or a specific target project using the Visual Studio build manager.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <param name="build">The build.</param>
    /// <param name="target">The target.</param>
    private void StartClean(DTE2 dte, SolutionBuild build, Project? target)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (target is null)
        {
            build.Clean(false);

            return;
        }
        var solution = GetShellService<IVsSolution>(typeof(SVsSolution));
        ErrorHandler.ThrowOnFailure(solution.GetProjectOfUniqueName(target.UniqueName, out var hierarchy));
        var manager = GetShellService<IVsSolutionBuildManager2>(typeof(SVsSolutionBuildManager));
        ErrorHandler.ThrowOnFailure(manager.StartSimpleUpdateProjectConfiguration(hierarchy, null, null, (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_CLEAN, 0, 0));
    }

    /// <summary>
    /// Retrieves the matching solution configuration based on the provided name and optional platform, prioritizing the active platform if multiple matches exist.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The env dte80.solution configuration2? result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static EnvDTE80.SolutionConfiguration2? FindConfiguration(SolutionBuild build, string configuration)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var parts = configuration.Split(['|'], 2);
        var name = parts[0].Trim();
        var platform = parts.Length > 1 ? parts[1].Trim() : null;
        var activePlatform = (build.ActiveConfiguration as EnvDTE80.SolutionConfiguration2)?.PlatformName;

        EnvDTE80.SolutionConfiguration2? match = null;
        var available = new List<string>();
        foreach (EnvDTE80.SolutionConfiguration2 candidate in build.SolutionConfigurations)
        {
            available.Add(candidate.Name + "|" + candidate.PlatformName);
            if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (platform is not null && !string.Equals(candidate.PlatformName, platform, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is null || string.Equals(candidate.PlatformName, activePlatform, StringComparison.OrdinalIgnoreCase))
            {
                match = candidate;
            }
        }

        return match ?? throw new InvalidOperationException($"No solution configuration '{configuration}'. Available: {string.Join(", ", available)}.");
    }

    /// <summary>
    /// Determines whether two solution configurations are equivalent by comparing their names and platform names using a case-insensitive ordinal comparison.
    /// </summary>
    /// <param name="left">The left.</param>
    /// <param name="right">The right.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    private static bool SameConfiguration(SolutionConfiguration left, SolutionConfiguration right)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return left is EnvDTE80.SolutionConfiguration2 a && right is EnvDTE80.SolutionConfiguration2 b
            && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.PlatformName, b.PlatformName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Calculates the total number of projects within the specified solution build that are marked as buildable in the active configuration.
    /// </summary>
    /// <param name="build">The build.</param>
    /// <returns>The int result.</returns>
    private static int CountBuildableProjects(SolutionBuild build)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var count = 0;
        try
        {
            foreach (SolutionContext context in build.ActiveConfiguration.SolutionContexts)
            {
                if (context.ShouldBuild)
                {
                    count++;
                }
            }
        }
        catch (COMException)
        {
            return 0;
        }

        return count;
    }

    /// <summary>
    /// Cancels the currently active build operation if it is in progress by executing the Build.Cancel command via the Visual Studio automation object.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <param name="build">The build.</param>
    private static void CancelRunningBuild(DTE2 dte, SolutionBuild build)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (build.BuildState != vsBuildState.vsBuildStateInProgress)
        {
            return;
        }

        try
        {
            ExecuteNamedCommand(dte, "Build.Cancel", null);
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Asynchronously retrieves a read-only list of error items by switching to the UI thread.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public async Task<IReadOnlyList<ErrorItem>> GetErrorsAsync(CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);

        return ReadErrorList();
    }

    /// <summary>The Error List fills asynchronously after a build; reads until the entry count stops changing.</summary>
    private async Task<IReadOnlyList<ErrorItem>> ReadStableErrorListAsync(CancellationToken cancellationToken)
    {
        await SwitchToUiAsync(cancellationToken);
        var items = ReadErrorList();
        var stableReads = 1;
        for (var read = 1; read < ErrorListMaxReads && stableReads < ErrorListSettleReads; read++)
        {
            await Task.Delay(ErrorListReadInterval, cancellationToken);
            var next = ReadErrorList();
            stableReads = next.Count == items.Count ? stableReads + 1 : 1;
            items = next;
        }

        return items;
    }

    /// <summary>
    /// Retrieves the current list of errors from the shell&apos;s error list service and maps them to a collection of ErrorItem objects.
    /// </summary>
    /// <returns>A collection of iread only list items.</returns>
    private IReadOnlyList<ErrorItem> ReadErrorList()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var errorList = GetShellService<IErrorList>(typeof(SVsErrorList));
        var items = new List<ErrorItem>();
        foreach (var entry in errorList.TableControl.Entries)
        {
            var item = new ErrorItem
            {
                Severity = SeverityName(entry),
                Code = NullIfEmpty(Value<string>(entry, StandardTableKeyNames.ErrorCode)),
                Message = Value<string>(entry, StandardTableKeyNames.Text) ?? "",
                Path = NullIfEmpty(Value<string>(entry, StandardTableKeyNames.DocumentName)),
                Line = Value<int>(entry, StandardTableKeyNames.Line) + 1,
                Column = Value<int>(entry, StandardTableKeyNames.Column) + 1,
                Project = NullIfEmpty(Value<string>(entry, StandardTableKeyNames.ProjectName)),
            };
            if (item.Path is null)
            {
                item.Line = item.Column = 0;
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Resolves the error severity category of the specified table entry into its corresponding string representation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The string result.</returns>
    private static string SeverityName(ITableEntryHandle entry)
    {
        switch (Value<__VSERRORCATEGORY>(entry, StandardTableKeyNames.ErrorSeverity))
        {
            case __VSERRORCATEGORY.EC_ERROR: return "error";
            case __VSERRORCATEGORY.EC_WARNING: return "warning";
            default: return "message";
        }
    }

    /// <summary>
    /// Retrieves a value of the specified type from the table entry associated with the given key, returning the default value if the key is not found or the value is of an incompatible type.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="key">The key.</param>
    /// <returns>The t? result.</returns>
    private static T? Value<T>(ITableEntryHandle entry, string key) =>
        entry.TryGetValue(key, out var value) && value is T typed ? typed : default;

    /// <summary>
    /// Asynchronously retrieves a specified number of trailing lines from a designated or active Visual Studio Output window pane.
    /// </summary>
    /// <param name="pane">The pane.</param>
    /// <param name="maxLines">The max lines.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task<string> ReadOutputAsync(string? pane, int maxLines, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var window = dte.ToolWindows.OutputWindow;
        OutputWindowPane? active;
        try
        {
            active = pane is null ? window.ActivePane : null;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Visual Studio could not read the active Output pane: {ex.Message}", ex);
        }
        var selected = pane is null ? active : FindPane(window, pane);
        if (selected is null)
        {
            throw new InvalidOperationException($"No Output pane is active. Panes: {PaneNames(window)}.");
        }

        var document = selected.TextDocument ?? throw new InvalidOperationException($"The Output pane '{selected.Name}' cannot be read.");
        var lastLine = document.EndPoint.Line;
        var firstLine = Math.Max(1, lastLine - Math.Max(1, maxLines) + 1);
        var start = document.StartPoint.CreateEditPoint();
        start.MoveToLineAndOffset(firstLine, 1);
        var text = start.GetText(document.EndPoint).TrimEnd('\r', '\n');
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines.Count > maxLines)
        {
            lines = [.. lines.Skip(lines.Count - maxLines)];
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Searches for an output window pane by name, returning an exact match or a partial match, and throws an &lt;see cref=&quot;InvalidOperationException&quot;/&gt; if no suitable pane is found.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="name">The name.</param>
    /// <returns>The output window pane result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static OutputWindowPane FindPane(OutputWindow window, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OutputWindowPane? partial = null;
        foreach (OutputWindowPane candidate in window.OutputWindowPanes)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (partial is null && candidate.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                partial = candidate;
            }
        }

        return partial ?? throw new InvalidOperationException($"No Output pane named '{name}'. Panes: {PaneNames(window)}.");
    }

    /// <summary>
    /// Retrieves a comma-separated string containing the names of all panes associated with the specified output window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The string result.</returns>
    private static string PaneNames(OutputWindow window)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var names = new List<string>();
        foreach (OutputWindowPane candidate in window.OutputWindowPanes)
        {
            names.Add(candidate.Name);
        }

        return string.Join(", ", names);
    }
}
