using System;
using System.Collections.Generic;

namespace Omp.Core.Processes;

internal sealed class OmpProcessOptions
{
    public string Executable { get; set; } = "";

    /// <summary>Arguments appended after <c>--mode rpc-ui</c>.</summary>
    public IReadOnlyList<string> Args { get; set; } = Array.Empty<string>();

    public string Cwd { get; set; } = "";

    /// <summary>Overrides of the host environment; a null value removes a variable.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; set; }

    public IOmpLogger Logger { get; set; } = null!;

    /// <summary>Whether the process tree is placed in a kill-on-close job object; tests turn it off to exercise the tree-walk path.</summary>
    public bool UseJobObject { get; set; } = true;

    /// <summary>The operating system calls the process tree walk makes; null uses Windows'. Tests substitute their own.</summary>
    internal ProcessTree.INative? TreeNative { get; set; }

    /// <summary>The job object and thread calls the launch makes; null uses Windows'. Tests substitute their own.</summary>
    internal IProcessNative? Native { get; set; }
}
