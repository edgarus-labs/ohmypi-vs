using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Shell.TableControl;
using OhMyPi.VisualStudio.Logic.Automation;
using DTE2 = EnvDTE80.DTE2;
using ErrorItem = OhMyPi.VisualStudio.Logic.Automation.ErrorItem;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio
{
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
                    if (success) Succeeded++;
                    else Failed++;
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
            if (dte.Solution == null || !dte.Solution.IsOpen) throw new InvalidOperationException("No solution is open.");
            var build = dte.Solution.SolutionBuild;
            if (build.BuildState == vsBuildState.vsBuildStateInProgress) throw new InvalidOperationException("A build is already running.");
            if (dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode) throw new InvalidOperationException("The debugger is running; stop debugging before building.");

            var target = project == null ? null : FindProject(dte, project);
            var requested = configuration == null ? null : FindConfiguration(build, configuration);
            var previous = build.ActiveConfiguration;
            var switched = requested != null && !SameConfiguration(requested, previous);

            var expected = target == null ? CountBuildableProjects(build) : 0;
            var result = new BuildResult();
            using (var linked = LinkToDisposal(cancellationToken))
            using (var watcher = new BuildWatcher(dte))
            {
                var failed = true;
                try
                {
                    if (switched) requested!.Activate();
                    if (action == BuildAction.Clean || action == BuildAction.Rebuild)
                    {
                        await RunBuildStepAsync(dte, build, watcher, () => StartClean(dte, build, target), linked.Token);
                        if (action == BuildAction.Rebuild) watcher.Reset();
                    }
                    if (action == BuildAction.Build || action == BuildAction.Rebuild)
                        await RunBuildStepAsync(dte, build, watcher, () => StartBuild(build, target), linked.Token);
                    failed = false;
                }
                catch (OperationCanceledException)
                {
                    CancelRunningBuild(dte, build);
                    throw;
                }
                finally
                {
                    if (switched) RestoreConfiguration(previous, failed);
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
                if (result.Errors.Any(item => item.Severity == "error")) result.Succeeded = false;
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
                if (!swallow) throw new InvalidOperationException($"Visual Studio could not restore the previous solution configuration: {ex.Message}", ex);
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
                if (state == vsBuildState.vsBuildStateInProgress) sawProgress = true;
                else if (state == vsBuildState.vsBuildStateDone && sawProgress) return;
            }
        }

        private void StartBuild(SolutionBuild build, Project? target)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (target == null) build.Build(false);
            else build.BuildProject(build.ActiveConfiguration.Name, target.UniqueName, false);
        }

        private void StartClean(DTE2 dte, SolutionBuild build, Project? target)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (target == null)
            {
                build.Clean(false);
                return;
            }
            var solution = GetShellService<IVsSolution>(typeof(SVsSolution));
            ErrorHandler.ThrowOnFailure(solution.GetProjectOfUniqueName(target.UniqueName, out var hierarchy));
            var manager = GetShellService<IVsSolutionBuildManager2>(typeof(SVsSolutionBuildManager));
            ErrorHandler.ThrowOnFailure(manager.StartSimpleUpdateProjectConfiguration(hierarchy, null, null, (uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_CLEAN, 0, 0));
        }

        private static EnvDTE80.SolutionConfiguration2? FindConfiguration(SolutionBuild build, string configuration)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var parts = configuration.Split(new[] { '|' }, 2);
            var name = parts[0].Trim();
            var platform = parts.Length > 1 ? parts[1].Trim() : null;
            var activePlatform = (build.ActiveConfiguration as EnvDTE80.SolutionConfiguration2)?.PlatformName;

            EnvDTE80.SolutionConfiguration2? match = null;
            var available = new List<string>();
            foreach (EnvDTE80.SolutionConfiguration2 candidate in build.SolutionConfigurations)
            {
                available.Add(candidate.Name + "|" + candidate.PlatformName);
                if (!string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (platform != null && !string.Equals(candidate.PlatformName, platform, StringComparison.OrdinalIgnoreCase)) continue;
                if (match == null || string.Equals(candidate.PlatformName, activePlatform, StringComparison.OrdinalIgnoreCase)) match = candidate;
            }
            return match ?? throw new InvalidOperationException($"No solution configuration '{configuration}'. Available: {string.Join(", ", available)}.");
        }

        private static bool SameConfiguration(SolutionConfiguration left, SolutionConfiguration right)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return left is EnvDTE80.SolutionConfiguration2 a && right is EnvDTE80.SolutionConfiguration2 b
                && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.PlatformName, b.PlatformName, StringComparison.OrdinalIgnoreCase);
        }

        private static int CountBuildableProjects(SolutionBuild build)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var count = 0;
            try
            {
                foreach (SolutionContext context in build.ActiveConfiguration.SolutionContexts)
                {
                    if (context.ShouldBuild) count++;
                }
            }
            catch (COMException)
            {
                return 0;
            }
            return count;
        }

        private static void CancelRunningBuild(DTE2 dte, SolutionBuild build)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (build.BuildState != vsBuildState.vsBuildStateInProgress) return;
            try
            {
                ExecuteNamedCommand(dte, "Build.Cancel", null);
            }
            catch (InvalidOperationException)
            {
            }
        }

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
                if (item.Path == null) item.Line = item.Column = 0;
                items.Add(item);
            }
            return items;
        }

        private static string SeverityName(ITableEntryHandle entry)
        {
            switch (Value<__VSERRORCATEGORY>(entry, StandardTableKeyNames.ErrorSeverity))
            {
                case __VSERRORCATEGORY.EC_ERROR: return "error";
                case __VSERRORCATEGORY.EC_WARNING: return "warning";
                default: return "message";
            }
        }

        private static T? Value<T>(ITableEntryHandle entry, string key) =>
            entry.TryGetValue(key, out var value) && value is T typed ? typed : default;

        public async Task<string> ReadOutputAsync(string? pane, int maxLines, CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            var window = dte.ToolWindows.OutputWindow;
            OutputWindowPane? active;
            try
            {
                active = pane == null ? window.ActivePane : null;
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Visual Studio could not read the active Output pane: {ex.Message}", ex);
            }
            var selected = pane == null ? active : FindPane(window, pane);
            if (selected == null) throw new InvalidOperationException($"No Output pane is active. Panes: {PaneNames(window)}.");

            var document = selected.TextDocument ?? throw new InvalidOperationException($"The Output pane '{selected.Name}' cannot be read.");
            var lastLine = document.EndPoint.Line;
            var firstLine = Math.Max(1, lastLine - Math.Max(1, maxLines) + 1);
            var start = document.StartPoint.CreateEditPoint();
            start.MoveToLineAndOffset(firstLine, 1);
            var text = start.GetText(document.EndPoint).TrimEnd('\r', '\n');
            var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            if (lines.Count > maxLines) lines = lines.Skip(lines.Count - maxLines).ToList();
            return string.Join("\n", lines);
        }

        private static OutputWindowPane FindPane(OutputWindow window, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            OutputWindowPane? partial = null;
            foreach (OutputWindowPane candidate in window.OutputWindowPanes)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) return candidate;
                if (partial == null && candidate.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) partial = candidate;
            }
            return partial ?? throw new InvalidOperationException($"No Output pane named '{name}'. Panes: {PaneNames(window)}.");
        }

        private static string PaneNames(OutputWindow window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var names = new List<string>();
            foreach (OutputWindowPane candidate in window.OutputWindowPanes) names.Add(candidate.Name);
            return string.Join(", ", names);
        }
    }
}
