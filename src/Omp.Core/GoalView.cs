namespace Omp.Core;

/// <summary>What OMP's <c>goal</c> command does with the session's goal.</summary>
public enum GoalOperation { Get, Create, Pause, Resume, Drop }

/// <summary>The session's goal as OMP reports it.</summary>
public sealed class GoalView
{
    public string Objective { get; set; } = "";

    /// <summary>active, paused, budget-limited, complete or dropped.</summary>
    public string Status { get; set; } = "";

    public long TokensUsed { get; set; }

    public long? TokenBudget { get; set; }
}
