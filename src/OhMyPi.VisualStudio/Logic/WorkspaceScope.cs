using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>The working directory of one OMP service and the directories whose files may be opened without asking.</summary>
internal sealed class WorkspaceScope
{
    /// <summary>
    /// Initializes a new instance of the WorkspaceScope class with the specified current working directory and root paths.
    /// </summary>
    /// <param name="cwd">The cwd.</param>
    /// <param name="roots">The collection of roots.</param>
    public WorkspaceScope(string cwd, IReadOnlyList<string> roots)
    {
        Cwd = cwd;
        Roots = roots;
    }

    /// <summary>
    /// Gets the cwd.
    /// </summary>
    public string Cwd { get; }

    /// <summary>
    /// Gets the collection of roots.
    /// </summary>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>The absolute path a tool path names, relative paths resolved against <see cref="Cwd"/>.</summary>
    /// <exception cref="InvalidOperationException">The path names no local file.</exception>
    public string Resolve(string rawPath) =>
        ChangePaths.ResolveToolPath(rawPath, Cwd) ?? throw new InvalidOperationException($"Not a local file: {rawPath}");

    /// <summary>
    /// Determines whether the specified path is located within the defined root directories.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool Contains(string path) => ChangePaths.IsWithinRoots(path, Roots);

    /// <summary>
    /// Whether a path named by OMP may be opened: always inside the workspace, otherwise only when
    /// <paramref name="confirmOutside"/> agrees, because opening reads the file and a UNC path reaches the network.
    /// </summary>
    public async Task<bool> MayOpenAsync(string path, Func<string, Task<bool>> confirmOutside) =>
        Contains(path) || await confirmOutside(path).ConfigureAwait(false);
}
