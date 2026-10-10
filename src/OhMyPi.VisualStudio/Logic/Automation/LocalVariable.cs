namespace OhMyPi.VisualStudio.Logic.Automation;

/// <summary>
/// Represents a local variable within a code block, encapsulating its name, type, and assigned value.
/// </summary>
internal sealed class LocalVariable
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    public string? Value { get; set; }
}
