namespace Omp.Core;

/// <summary>USD per million tokens; a field OMP did not report is null, never zero.</summary>
public sealed class ModelCostView
{
    /// <summary>
    /// Gets or sets the input.
    /// </summary>
    public double? Input { get; set; }

    /// <summary>
    /// Gets or sets the output.
    /// </summary>
    public double? Output { get; set; }
}
