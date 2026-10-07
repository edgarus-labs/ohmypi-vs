using Omp.Core.Changes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>The working directory of one OMP service and the directories whose files may be opened without asking.</summary>
internal sealed class WorkspaceScope
{
    public WorkspaceScope(string cwd, IReadOnlyList<string> roots)
    {
        Cwd = cwd;
        Roots = roots;
    }

    public string Cwd { get; }

    public IReadOnlyList<string> Roots { get; }

    /// <summary>The absolute path a tool path names, relative paths resolved against <see cref="Cwd"/>.</summary>
    /// <exception cref="InvalidOperationException">The path names no local file.</exception>
    public string Resolve(string rawPath) =>
        ChangePaths.ResolveToolPath(rawPath, Cwd) ?? throw new InvalidOperationException($"Not a local file: {rawPath}");

    public bool Contains(string path) => ChangePaths.IsWithinRoots(path, Roots);

    /// <summary>
    /// Whether a path named by OMP may be opened: always inside the workspace, otherwise only when
    /// <paramref name="confirmOutside"/> agrees, because opening reads the file and a UNC path reaches the network.
    /// </summary>
    public async Task<bool> MayOpenAsync(string path, Func<string, Task<bool>> confirmOutside) =>
        Contains(path) || await confirmOutside(path).ConfigureAwait(false);
}
