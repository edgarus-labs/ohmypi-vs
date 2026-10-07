namespace Omp.Core;

/// <summary>
/// Prompt/session lifecycle.
/// Idle → Submitting (prompt sent, not admitted) → Running (admitted / agent_start)
/// → Yielded (prompt_result received, session not settled) → Idle (session_settled,
/// prompt_result with sessionSettled=true, or agentInvoked=false).
/// Aborting is entered by AbortAsync and left on the next prompt_result/agent_end/session_settled.
/// </summary>
public enum SessionPhase { Idle, Submitting, Running, Yielded, Aborting }
