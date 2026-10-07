using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Omp.Core;

/// <summary>The tools a client provides to OMP's agent.</summary>
public interface IHostTools
{
    IReadOnlyList<HostToolDefinition> Definitions { get; }

    /// <summary>Runs <paramref name="name"/>; a thrown exception is reported to the agent as a tool error.</summary>
    System.Threading.Tasks.Task<HostToolResult> InvokeAsync(string name, JObject arguments, System.Threading.CancellationToken cancellationToken);
}
