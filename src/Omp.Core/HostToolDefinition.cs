using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>A tool the client provides: OMP lists it to the model and calls back into the client to run it.</summary>
public sealed class HostToolDefinition
{
    public HostToolDefinition(string name, string description, JObject parameters)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>JSON schema of the tool's arguments.</summary>
    public JObject Parameters { get; }
}
