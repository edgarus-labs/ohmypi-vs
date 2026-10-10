using Newtonsoft.Json.Linq;

namespace Omp.Core;

/// <summary>A tool the client provides: OMP lists it to the model and calls back into the client to run it.</summary>
public sealed class HostToolDefinition
{
    /// <summary>
    /// Initializes a new instance of the HostToolDefinition class with the specified name, description, and parameters.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="parameters">The parameters.</param>
    public HostToolDefinition(string name, string description, JObject parameters)
    {
        Name = name;
        Description = description;
        Parameters = parameters;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string Description { get; }

    /// <summary>JSON schema of the tool's arguments.</summary>
    public JObject Parameters { get; }
}
