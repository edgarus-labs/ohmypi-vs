namespace Omp.Core;

/// <summary>USD per million tokens; a field OMP did not report is null, never zero.</summary>
public sealed class ModelCostView
{
    public double? Input { get; set; }

    public double? Output { get; set; }
}
