using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Omp.Core;

/// <summary>
/// Owns the OMP child process, the RPC client and the session/agent state that survives
/// restarts. Events are raised on the thread that caused the change: OMP's reader thread for OMP frames, a
/// thread-pool thread for timers and refreshes, and the caller's thread for changes a method makes before its first
/// await (for example the echo row and phase change of <see cref="PromptAsync"/>, or the state change of
/// <see cref="StartAsync"/>). Handlers run while the service's lock is held, so they must return quickly, must not
/// block on another thread that might call into the service, and UI consumers marshal to their own thread.
/// Snapshot properties are safe to read from any thread.
/// </summary>
public interface IOmpService : IDisposable
{
    string Cwd { get; }

    ConnectionStatus Connection { get; }

    SessionView Session { get; }

    IReadOnlyList<TranscriptItem> Transcript { get; }

    IReadOnlyList<AgentView> Agents { get; }

    /// <summary>Slash commands OMP currently accepts, in OMP's order.</summary>
    IReadOnlyList<SlashCommandView> Commands { get; }

    /// <summary>Texts OMP extensions currently show via <c>setStatus</c>, by key; a UI attached late shows these.</summary>
    IReadOnlyDictionary<string, string> Statuses { get; }

    /// <summary>Interactions OMP is still waiting for, in arrival order; a UI attached late shows these.</summary>
    IReadOnlyList<InteractionRequest> PendingInteractions { get; }

    event EventHandler<ConnectionStatus> ConnectionChanged;

    event EventHandler<SessionView> SessionChanged;

    /// <summary>The item that was added or changed (streaming updates included).</summary>
    event EventHandler<TranscriptItem> TranscriptItemChanged;

    /// <summary>The transcript was replaced wholesale (new/switched session, history load).</summary>
    event EventHandler<IReadOnlyList<TranscriptItem>> TranscriptReset;

    event EventHandler<IReadOnlyList<AgentView>> AgentsChanged;

    event EventHandler<ToolExecutionEvent> ToolExecution;

    event EventHandler<InteractionRequest> InteractionRequested;

    /// <summary>OMP withdrew a pending interaction (timeout/abort); argument is the request id.</summary>
    event EventHandler<string> InteractionCancelled;

    event EventHandler<PresentationRequest> Presentation;

    event EventHandler<IReadOnlyList<SlashCommandView>> CommandsChanged;

    /// <summary>Spawn, handshake, configure subscriptions, optionally bind a session.</summary>
    Task StartAsync(StartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Stop and start again, re-binding the current session file.</summary>
    Task RestartAsync();

    /// <summary>
    /// Graceful shutdown: close stdin, wait, then terminate the whole process tree. Fails, without logging, when the
    /// tree survives being killed; the caller reports it.
    /// </summary>
    Task StopAsync();

    Task<IReadOnlyList<ModelView>> ListModelsAsync();

    /// <summary>
    /// The configured providers and their rate limits, from OMP's <c>/usage</c> command; the report does not enter
    /// the transcript. Fails with <see cref="InvalidOperationException"/> when OMP prints no usage report.
    /// </summary>
    Task<IReadOnlyList<ProviderUsage>> GetUsageAsync();

    Task SetModelAsync(string provider, string modelId);

    Task SetThinkingLevelAsync(string level);

    Task SetFastModeAsync(bool enabled);

    /// <summary>Completes at prompt completion (prompt_result or local completion), never at acknowledgement.</summary>
    Task<PromptOutcome> PromptAsync(string text, PromptMode mode = PromptMode.Auto, IReadOnlyList<PromptImage>? images = null);

    Task AbortAsync();

    Task NewSessionAsync();

    Task SwitchSessionAsync(string sessionFile);

    Task SetSessionNameAsync(string name);

    /// <summary>Sessions stored next to the current session file, newest first.</summary>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync();

    void RespondInteraction(string id, InteractionResponse response);

    Task<bool> CancelAgentAsync(string agentId);

    /// <summary>
    /// Sends <paramref name="message"/> to an agent. For "main" it is a prompt (a steering message while the agent
    /// works) and completes once OMP accepted it; it throws <see cref="OmpRequestException"/> when OMP refuses it.
    /// </summary>
    Task SteerAgentAsync(string agentId, string message);

    /// <summary>Transcript of a subagent ("main" returns the current session transcript).</summary>
    Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId);
}
