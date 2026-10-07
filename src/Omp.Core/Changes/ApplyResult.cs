namespace Omp.Core.Changes;

/// <summary>What changed in the model after one tool event.</summary>
public sealed class ApplyResult
{
    /// <summary>The set of tracked changes or one of their rows changed.</summary>
    public bool ChangesChanged { get; set; }
}
