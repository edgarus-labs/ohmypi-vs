using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OhMyPi.VisualStudio.Logic.Automation;
using DTE2 = EnvDTE80.DTE2;
using Task = System.Threading.Tasks.Task;

namespace OhMyPi.VisualStudio
{
    /// <summary>
    /// Drives Visual Studio for the OMP host tools through DTE and the shell services. Every method switches to the UI
    /// thread first; waits (builds, debugger transitions) are asynchronous so the UI thread stays free.
    /// </summary>
    internal sealed partial class VsAutomation : IVsAutomation
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan BuildPollInterval = TimeSpan.FromSeconds(1);
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        private readonly OmpPackage _package;

        public VsAutomation(OmpPackage package)
        {
            _package = package;
        }

        private JoinableTaskFactory Jtf => _package.JoinableTaskFactory;

        private async Task SwitchToUiAsync(CancellationToken cancellationToken)
        {
            await Jtf.SwitchToMainThreadAsync(_package.DisposalToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task<DTE2> EnterUiAsync(CancellationToken cancellationToken)
        {
            await SwitchToUiAsync(cancellationToken);
            return GetDte();
        }

        private DTE2 GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ((System.IServiceProvider)_package).GetService(typeof(SDTE)) as DTE2
                ?? throw new InvalidOperationException("Visual Studio automation (DTE) is not available.");
        }

        private TService GetShellService<TService>(Type serviceType) where TService : class
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ((System.IServiceProvider)_package).GetService(serviceType) as TService
                ?? throw new InvalidOperationException($"The Visual Studio service {serviceType.Name} is not available.");
        }

        private CancellationTokenSource LinkToDisposal(CancellationToken cancellationToken) =>
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _package.DisposalToken);

        private async Task<bool> WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using (var linked = LinkToDisposal(cancellationToken))
            {
#pragma warning disable VSTHRD003 // The task is completed by a DTE event handler that runs on the UI thread this method resumes on, never by work that needs it.
                return await Waiting.WaitAsync(task, timeout, linked.Token);
#pragma warning restore VSTHRD003
            }
        }

        private static bool IsRootedPath(string? path) =>
            !string.IsNullOrEmpty(path) && path!.IndexOfAny(InvalidPathChars) < 0 && Path.IsPathRooted(path);

        private static bool SamePath(string? left, string? right)
        {
            if (left == null || right == null) return false;
            if (!IsRootedPath(left) || !IsRootedPath(right)) return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string FullPath(string path)
        {
            if (!IsRootedPath(path)) throw new InvalidOperationException($"Not an absolute file path: {path}");
            return Path.GetFullPath(path);
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        public async Task<SolutionInfo> GetSolutionAsync(CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            var info = new SolutionInfo { ActiveDocument = ActiveDocumentPath(dte) };
            var solution = dte.Solution;
            if (solution == null || !solution.IsOpen) return info;

            info.Path = NullIfEmpty(solution.FullName);
            var projects = AllProjects(dte);
            foreach (var project in projects)
            {
                var entry = new ProjectInfo { Name = SafeName(project), Path = SafeProjectPath(project), Kind = KindName(SafeKind(project)) };
                info.Projects.Add(entry);
            }

            var build = solution.SolutionBuild;
            try
            {
                if (build.ActiveConfiguration is EnvDTE80.SolutionConfiguration2 configuration)
                {
                    info.ActiveConfiguration = configuration.Name;
                    info.ActivePlatform = configuration.PlatformName;
                }
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Visual Studio could not read the active solution configuration: {ex.Message}", ex);
            }
            if (build.StartupProjects is Array startup && startup.Length > 0)
            {
                var names = new List<string>();
                foreach (var unique in startup.OfType<string>()) names.Add(ProjectNameByUniqueName(projects, unique));
                info.StartupProject = string.Join(", ", names);
            }
            return info;
        }

        private static string ProjectNameByUniqueName(List<Project> projects, string uniqueName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (var project in projects)
            {
                if (SafeUniqueName(project) == uniqueName) return SafeName(project);
            }
            return uniqueName;
        }

        public async Task<IReadOnlyList<string>> FindCommandsAsync(string filter, int max, CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            var names = new List<string>();
            var visited = 0;
            foreach (Command command in dte.Commands)
            {
                if (++visited % 500 == 0)
                {
                    await Jtf.SwitchToMainThreadAsync(alwaysYield: true, _package.DisposalToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var name = command.Name;
                if (!string.IsNullOrEmpty(name) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) names.Add(name);
            }
            return names
                .OrderBy(name => name.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(name => name.Length)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, max))
                .ToList();
        }

        public async Task ExecuteCommandAsync(string command, string? arguments, CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            ExecuteNamedCommand(dte, command, arguments);
        }

        private static void ExecuteNamedCommand(DTE2 dte, string command, string? arguments)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Command? known;
            try
            {
                known = dte.Commands.Item(command);
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException($"Unknown command '{command}'. Use vs_find_commands to look up command names.");
            }
            if (!known.IsAvailable) throw new InvalidOperationException($"The command '{command}' is not available in the current context of Visual Studio.");
            try
            {
                dte.ExecuteCommand(command, arguments ?? "");
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"The command '{command}' failed: {ex.Message}", ex);
            }
        }
    }
}
