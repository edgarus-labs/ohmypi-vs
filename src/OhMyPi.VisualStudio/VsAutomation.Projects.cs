using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using DTE2 = EnvDTE80.DTE2;

namespace OhMyPi.VisualStudio
{
    internal sealed partial class VsAutomation
    {
        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private static readonly Dictionary<string, string> ProjectKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"] = "C#",
            ["{9A19103F-16F7-4668-BE54-9A1E7A4F7556}"] = "C# (SDK-style)",
            ["{F184B08F-C81C-45F6-A57F-5ABD9991F28F}"] = "Visual Basic",
            ["{778DAE3C-4631-46EA-AA77-85C1314464D9}"] = "Visual Basic (SDK-style)",
            ["{F2A71F9B-5D33-465A-A702-920D77279786}"] = "F#",
            ["{6EC3EE1D-3C4E-46DD-8F32-0CC8E7565705}"] = "F# (SDK-style)",
            ["{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}"] = "C++",
        };

        private static string? KindName(string? kind) => kind != null && ProjectKinds.TryGetValue(kind, out var name) ? name : kind;

        private static string SafeName(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return project.Name; }
            catch (COMException) { return "(unavailable project)"; }
        }

        private static string? SafeUniqueName(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return project.UniqueName; }
            catch (COMException) { return null; }
        }

        private static string? SafeKind(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return project.Kind; }
            catch (COMException) { return null; }
        }

        private static string? SafeProjectPath(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return NullIfEmpty(project.FullName); }
            catch (COMException) { return null; }
            catch (NotImplementedException) { return null; }
        }

        private static string? ActiveDocumentPath(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return NullIfEmpty(dte.ActiveDocument?.FullName); }
            catch (COMException) { return null; }
        }

        /// <summary>Every project of the solution, solution folders flattened.</summary>
        private static List<Project> AllProjects(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<Project>();
            foreach (Project project in dte.Solution.Projects) Collect(project, result);
            return result;
        }

        private static void Collect(Project? project, List<Project> result)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return;
            if (SafeKind(project) != SolutionFolderKind)
            {
                result.Add(project);
                return;
            }
            ProjectItems? items;
            try { items = project.ProjectItems; }
            catch (COMException) { return; }
            if (items == null) return;
            foreach (ProjectItem item in items)
            {
                Project? sub;
                try { sub = item.SubProject; }
                catch (COMException) { continue; }
                Collect(sub, result);
            }
        }

        private static Project FindProject(DTE2 dte, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte.Solution == null || !dte.Solution.IsOpen) throw new InvalidOperationException("No solution is open.");
            var projects = AllProjects(dte);
            var matches = projects.Where(project => string.Equals(SafeName(project), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SafeUniqueName(project), name, StringComparison.OrdinalIgnoreCase)
                || SamePath(SafeProjectPath(project), name)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) throw new InvalidOperationException($"More than one project matches '{name}'; use its unique name or full path: {string.Join(", ", matches.Select(SafeUniqueName))}.");
            throw new InvalidOperationException($"No project named '{name}'. Projects: {string.Join(", ", projects.Select(SafeName))}.");
        }

        private static string? ItemPath(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return item.FileCount > 0 ? item.FileNames[1] : null; }
            catch (COMException) { return null; }
            catch (ArgumentException) { return null; }
        }

        private static ProjectItem? FindItem(ProjectItems? items, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return null;
            foreach (ProjectItem item in items)
            {
                if (SamePath(ItemPath(item), path)) return item;
                ProjectItems? children;
                try { children = item.ProjectItems; }
                catch (COMException) { continue; }
                var found = FindItem(children, path);
                if (found != null) return found;
            }
            return null;
        }

        public async Task AddFileToProjectAsync(string project, string path, CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            var file = FullPath(path);
            if (!System.IO.File.Exists(file)) throw new InvalidOperationException($"File not found: {file}. Create it first, then add it to the project.");
            var target = FindProject(dte, project);
            if (FindItem(target.ProjectItems, file) != null) throw new InvalidOperationException($"{file} is already part of project '{SafeName(target)}'.");
            try
            {
                target.ProjectItems.AddFromFile(file);
                target.Save();
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Visual Studio could not add {file} to project '{SafeName(target)}': {ex.Message}", ex);
            }
        }

        public async Task RemoveFileFromProjectAsync(string project, string path, CancellationToken cancellationToken)
        {
            var dte = await EnterUiAsync(cancellationToken);
            var file = FullPath(path);
            var target = FindProject(dte, project);
            var item = FindItem(target.ProjectItems, file) ?? throw new InvalidOperationException($"{file} is not part of project '{SafeName(target)}'.");
            try
            {
                item.Remove();
                target.Save();
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Visual Studio could not remove {file} from project '{SafeName(target)}': {ex.Message}", ex);
            }
        }
    }
}
