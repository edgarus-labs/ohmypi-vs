using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTE2 = EnvDTE80.DTE2;

namespace OhMyPi.VisualStudio;

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

    /// <summary>
    /// Resolves a project kind identifier to its corresponding display name using the ProjectKinds lookup table, returning the original identifier if no mapping is found.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The string? result.</returns>
    private static string? KindName(string? kind) => kind is not null && ProjectKinds.TryGetValue(kind, out var name) ? name : kind;

    /// <summary>
    /// Retrieves the name of the specified project, returning a fallback string if a COM exception occurs during access.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The string result.</returns>
    private static string SafeName(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return project.Name; }
        catch (COMException) { return "(unavailable project)"; }
    }

    /// <summary>
    /// Retrieves the unique name of the specified project, returning null if a COM exception occurs during access.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The string? result.</returns>
    private static string? SafeUniqueName(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return project.UniqueName; }
        catch (COMException) { return null; }
    }

    /// <summary>
    /// Retrieves the project kind while suppressing COM exceptions that may occur during access.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The string? result.</returns>
    private static string? SafeKind(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return project.Kind; }
        catch (COMException) { return null; }
    }

    /// <summary>
    /// Retrieves the full file path of the specified project, returning null if the path is empty or if a COM or implementation error occurs.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The string? result.</returns>
    private static string? SafeProjectPath(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return NullIfEmpty(project.FullName); }
        catch (COMException) { return null; }
        catch (NotImplementedException) { return null; }
    }

    /// <summary>
    /// Retrieves the full file system path of the currently active document from the specified DTE2 instance, returning null if no document is active or a COM exception occurs.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <returns>The string? result.</returns>
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
        foreach (Project project in dte.Solution.Projects)
        {
            Collect(project, result);
        }

        return result;
    }

    /// <summary>
    /// Recursively traverses a project hierarchy to collect all non-folder projects into the specified result list.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="result">The collection of result.</param>
    private static void Collect(Project? project, List<Project> result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (project is null)
        {
            return;
        }

        if (SafeKind(project) != SolutionFolderKind)
        {
            result.Add(project);

            return;
        }
        ProjectItems? items;
        try { items = project.ProjectItems; }
        catch (COMException) { return; }
        if (items is null)
        {
            return;
        }

        foreach (ProjectItem item in items)
        {
            Project? sub;
            try { sub = item.SubProject; }
            catch (COMException) { continue; }
            Collect(sub, result);
        }
    }

    /// <summary>
    /// Searches for a project within the open Visual Studio solution by matching the provided name against the project&apos;s display name, unique name, or file path.
    /// </summary>
    /// <param name="dte">The dte.</param>
    /// <param name="name">The name.</param>
    /// <returns>The project result.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    private static Project FindProject(DTE2 dte, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (dte.Solution is null || !dte.Solution.IsOpen)
        {
            throw new InvalidOperationException("No solution is open.");
        }

        var projects = AllProjects(dte);
        var matches = projects.Where(project => string.Equals(SafeName(project), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SafeUniqueName(project), name, StringComparison.OrdinalIgnoreCase)
            || SamePath(SafeProjectPath(project), name)).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"More than one project matches '{name}'; use its unique name or full path: {string.Join(", ", matches.Select(SafeUniqueName))}.");
        }

        throw new InvalidOperationException($"No project named '{name}'. Projects: {string.Join(", ", projects.Select(SafeName))}.");
    }

    /// <summary>
    /// Retrieves the file system path of the specified project item if it exists, returning null if the item is not a file or if a COM error occurs.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The string? result.</returns>
    private static string? ItemPath(ProjectItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try { return item.FileCount > 0 ? item.FileNames[1] : null; }
        catch (COMException) { return null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// Recursively searches the project item hierarchy to find and return the project item that matches the specified path.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="path">The path.</param>
    /// <returns>The project item? result.</returns>
    private static ProjectItem? FindItem(ProjectItems? items, string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (items is null)
        {
            return null;
        }

        foreach (ProjectItem item in items)
        {
            if (SamePath(ItemPath(item), path))
            {
                return item;
            }

            ProjectItems? children;
            try { children = item.ProjectItems; }
            catch (COMException) { continue; }
            var found = FindItem(children, path);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Asynchronously adds a specified file to the designated project within the Visual Studio environment after validating the file&apos;s existence and ensuring it is not already included.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
    public async Task AddFileToProjectAsync(string project, string path, CancellationToken cancellationToken)
    {
        var dte = await EnterUiAsync(cancellationToken);
        var file = FullPath(path);
        if (!System.IO.File.Exists(file))
        {
            throw new InvalidOperationException($"File not found: {file}. Create it first, then add it to the project.");
        }

        var target = FindProject(dte, project);
        if (FindItem(target.ProjectItems, file) is not null)
        {
            throw new InvalidOperationException($"{file} is already part of project '{SafeName(target)}'.");
        }

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

    /// <summary>
    /// Asynchronously removes a specified file from the designated project and saves the project changes.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an error occurs during execution.</exception>
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
