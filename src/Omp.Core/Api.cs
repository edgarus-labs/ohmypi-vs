using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Omp.Core
{
    /// <summary>Diagnostics sink. Implementations must never receive secrets; callers redact first.</summary>
    public interface IOmpLogger
    {
        void Error(string message, Exception? error = null);
        void Warn(string message, Exception? error = null);
        void Info(string message, Exception? error = null);
        void Debug(string message, Exception? error = null);

        /// <summary>Whether RPC frames are traced right now; read per frame.</summary>
        bool TraceEnabled { get; }

        /// <summary>One already-redacted RPC frame. <paramref name="direction"/> is "in" or "out".</summary>
        void Trace(string direction, string frame);
    }

    public enum ConnectionState { Stopped, Starting, Ready, Restarting, Failed }

    public sealed class ConnectionStatus
    {
        public ConnectionState State { get; set; }
        public string? Detail { get; set; }
        public int? Pid { get; set; }
        public int? ProtocolVersion { get; set; }
    }

    /// <summary>
    /// Prompt/session lifecycle.
    /// Idle → Submitting (prompt sent, not admitted) → Running (admitted / agent_start)
    /// → Yielded (prompt_result received, session not settled) → Idle (session_settled,
    /// prompt_result with sessionSettled=true, or agentInvoked=false).
    /// Aborting is entered by AbortAsync and left on the next prompt_result/agent_end/session_settled.
    /// </summary>
    public enum SessionPhase { Idle, Submitting, Running, Yielded, Aborting }

    /// <summary>USD per million tokens; a field OMP did not report is null, never zero.</summary>
    public sealed class ModelCostView
    {
        public double? Input { get; set; }
        public double? Output { get; set; }
    }

    public sealed class ModelView
    {
        public string Provider { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Api { get; set; }
        public long? ContextWindow { get; set; }
        public long? MaxTokens { get; set; }
        public bool Reasoning { get; set; }
        public IReadOnlyList<string> ThinkingEfforts { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Input { get; set; } = Array.Empty<string>();
        /// <summary>USD per million tokens, when OMP reports pricing.</summary>
        public ModelCostView? Cost { get; set; }
        /// <summary>The model's vendor class as OMP reports it (anthropic, openai, …); null when unknown.</summary>
        public string? VendorClass { get; set; }
        /// <summary>The family within the class (opus, sonnet, gpt, …); null when unknown.</summary>
        public string? Family { get; set; }
        /// <summary>The model's version within its family, as "5.5.0"; null when unknown.</summary>
        public string? Revision { get; set; }
    }

    public sealed class TodoTaskView
    {
        public string Content { get; set; } = "";
        /// <summary>pending | in_progress | completed | abandoned | blocked | other OMP value.</summary>
        public string Status { get; set; } = "pending";
    }

    public sealed class TodoPhaseView
    {
        public string Name { get; set; } = "";
        public IReadOnlyList<TodoTaskView> Tasks { get; set; } = Array.Empty<TodoTaskView>();
    }

    public sealed class QueueView
    {
        public IReadOnlyList<string> Steering { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> FollowUp { get; set; } = Array.Empty<string>();
    }

    public sealed class ContextUsageView
    {
        public long Tokens { get; set; }
        public long ContextWindow { get; set; }
        public double Percent { get; set; }
    }

    /// <summary>Immutable snapshot published by the service; consumers must not mutate it.</summary>
    public sealed class SessionView
    {
        public SessionPhase Phase { get; set; }
        public string? SessionId { get; set; }
        public string? SessionFile { get; set; }
        public string? SessionName { get; set; }
        public ModelView? Model { get; set; }
        /// <summary>Effective level OMP uses; with auto selected, the provisional or resolved effort.</summary>
        public string? ThinkingLevel { get; set; }
        /// <summary>The user's selector (OMP "configured"): auto or an explicit level.</summary>
        public string? ThinkingSelector { get; set; }
        /// <summary>The effort auto resolved to for the current turn.</summary>
        public string? ThinkingResolved { get; set; }
        /// <summary>off, auto for reasoning models, then the model's efforts.</summary>
        public IReadOnlyList<string> AvailableThinkingLevels { get; set; } = Array.Empty<string>();
        public bool? FastModeEnabled { get; set; }
        public bool? FastModeActive { get; set; }
        public bool IsCompacting { get; set; }
        public QueueView Queue { get; set; } = new QueueView();
        public ContextUsageView? ContextUsage { get; set; }
        public IReadOnlyList<TodoPhaseView> Todos { get; set; } = Array.Empty<TodoPhaseView>();
        public double? CostUsd { get; set; }
        public string? LastError { get; set; }
    }

    public sealed class ToolResultView
    {
        public string Text { get; set; } = "";
        public bool IsError { get; set; }
        /// <summary>Raw tool details (tool-specific, e.g. edit's path/diff/oldText/perFileResults).</summary>
        public JToken? Details { get; set; }
    }

    public sealed class UsageView
    {
        public long Input { get; set; }
        public long Output { get; set; }
        public long CacheRead { get; set; }
        public long CacheWrite { get; set; }
        public double? CostUsd { get; set; }
    }

    public enum NoticeLevel { Info, Warning, Error }

    /// <summary>
    /// Transcript entry. The service publishes a fresh instance for every change; an item's
    /// <see cref="Id"/> is stable so views replace the previous instance with the same id.
    /// </summary>
    public abstract class TranscriptItem
    {
        public string Id { get; set; } = "";
    }

    public sealed class UserItem : TranscriptItem
    {
        public string Text { get; set; } = "";
        public int ImageCount { get; set; }
    }

    public sealed class AssistantItem : TranscriptItem
    {
        public string Text { get; set; } = "";
        public string Thinking { get; set; } = "";
        public bool Streaming { get; set; }
        public string? Model { get; set; }
        public string? StopReason { get; set; }
        public string? ErrorMessage { get; set; }
        public UsageView? Usage { get; set; }
    }

    public enum ToolStatus { Running, Done, Error }

    public sealed class ToolItem : TranscriptItem
    {
        public string Name { get; set; } = "";
        public JToken? Args { get; set; }
        public ToolStatus Status { get; set; }
        /// <summary>Latest streamed partial output, plain text.</summary>
        public string? Partial { get; set; }
        public ToolResultView? Result { get; set; }
        /// <summary>Unix milliseconds.</summary>
        public long StartedAt { get; set; }
        public long? EndedAt { get; set; }
    }

    public sealed class NoticeItem : TranscriptItem
    {
        public NoticeLevel Level { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class CommandOutputItem : TranscriptItem
    {
        public string Text { get; set; } = "";
    }

    /// <summary>Closes an agent turn: how long it took and what its assistant messages cost.</summary>
    public sealed class TurnSummaryItem : TranscriptItem
    {
        public long DurationMs { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long CacheReadTokens { get; set; }
        public long CacheWriteTokens { get; set; }
        /// <summary>Null when OMP reported no pricing for the turn's messages.</summary>
        public double? CostUsd { get; set; }
        public bool Aborted { get; set; }
    }

    public enum ToolExecutionPhase { Start, End }

    /// <summary>Emitted for every tool execution boundary; consumed by change tracking.</summary>
    public sealed class ToolExecutionEvent
    {
        public ToolExecutionPhase Phase { get; set; }
        public string ToolCallId { get; set; } = "";
        public string Name { get; set; } = "";
        public JToken? Args { get; set; }
        /// <summary>Present on End.</summary>
        public ToolResultView? Result { get; set; }
    }

    public enum PromptStatus { Completed, Aborted, Error, Local }

    public sealed class PromptOutcome
    {
        public PromptStatus Status { get; set; }
        public string? Error { get; set; }
        public bool SessionSettled { get; set; }
        /// <summary>
        /// OMP acknowledged the prompt, so its user message is part of the session even when the outcome is an error.
        /// False only when OMP refused the prompt before admitting it (or it could not be sent); the caller may offer
        /// the text again.
        /// </summary>
        public bool Admitted { get; set; }
    }

    /// <summary>Auto: a plain prompt when idle, a steering message while the agent works. FollowUp queues it instead.</summary>
    public enum PromptMode { Auto, FollowUp }

    public sealed class PromptImage
    {
        /// <summary>Base64-encoded bytes.</summary>
        public string Data { get; set; } = "";
        public string MimeType { get; set; } = "";
    }

    public sealed class SessionSummary
    {
        public string Path { get; set; } = "";
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? FirstMessage { get; set; }
        public string? Cwd { get; set; }
        /// <summary>Unix milliseconds.</summary>
        public long Modified { get; set; }
        public long Size { get; set; }
    }

    public enum AgentStatus { Pending, Running, Completed, Failed, Aborted }

    public sealed class AgentView
    {
        /// <summary>"main" for the session's own agent; otherwise the OMP subagent id.</summary>
        public string Id { get; set; } = "";
        public string? ParentId { get; set; }
        /// <summary>Agent type/name (task, scout, reviewer, ...).</summary>
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Model { get; set; }
        public AgentStatus Status { get; set; }
        /// <summary>Running tool or latest progress line.</summary>
        public string? Activity { get; set; }
        public int? ToolCount { get; set; }
        public long? Tokens { get; set; }
        public double? CostUsd { get; set; }
        public long? StartedAt { get; set; }
        public long? EndedAt { get; set; }
        public string? SessionFile { get; set; }
        public string? Error { get; set; }
    }

    public sealed class AskOptionView
    {
        public string Label { get; set; } = "";
        public string? Description { get; set; }
        public string? Preview { get; set; }
    }

    public sealed class AskQuestionView
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public string? Header { get; set; }
        public IReadOnlyList<AskOptionView> Options { get; set; } = Array.Empty<AskOptionView>();
        public bool Multi { get; set; }
        public int? Recommended { get; set; }
    }

    public abstract class InteractionRequest
    {
        public string Id { get; set; } = "";
        public int? TimeoutMs { get; set; }
    }

    public sealed class ConfirmRequest : InteractionRequest
    {
        public string Title { get; set; } = "";
        public string? Message { get; set; }
    }

    public sealed class SelectOptionView
    {
        public string Label { get; set; } = "";
        public string? Description { get; set; }
    }

    public sealed class SelectRequest : InteractionRequest
    {
        public string Title { get; set; } = "";
        public IReadOnlyList<SelectOptionView> Options { get; set; } = Array.Empty<SelectOptionView>();
    }

    public sealed class InputRequest : InteractionRequest
    {
        public string Title { get; set; } = "";
        public string? Placeholder { get; set; }
        /// <summary>Render masked; never echo or log the value.</summary>
        public bool Secret { get; set; }
    }

    public sealed class EditorRequest : InteractionRequest
    {
        public string Title { get; set; } = "";
        public string? Prefill { get; set; }
    }

    public sealed class AskRequest : InteractionRequest
    {
        public IReadOnlyList<AskQuestionView> Questions { get; set; } = Array.Empty<AskQuestionView>();
    }

    public sealed class AskAnswer
    {
        public string Id { get; set; } = "";
        public IReadOnlyList<string> SelectedOptions { get; set; } = Array.Empty<string>();
        public string? CustomInput { get; set; }
    }

    public abstract class InteractionResponse
    {
        public static InteractionResponse FromValue(string value) => new ValueResponse { Value = value };
        public static InteractionResponse FromConfirmed(bool confirmed) => new ConfirmedResponse { Confirmed = confirmed };
        public static InteractionResponse Cancelled() => new CancelledResponse();
        public static InteractionResponse FromAnswers(IReadOnlyList<AskAnswer> answers) => new AnswersResponse { Answers = answers };
    }

    public sealed class ValueResponse : InteractionResponse { public string Value { get; set; } = ""; }
    public sealed class ConfirmedResponse : InteractionResponse { public bool Confirmed { get; set; } }
    public sealed class CancelledResponse : InteractionResponse { }
    public sealed class AnswersResponse : InteractionResponse { public IReadOnlyList<AskAnswer> Answers { get; set; } = Array.Empty<AskAnswer>(); }

    /// <summary>Fire-and-forget presentation requests from OMP extensions/tools.</summary>
    public abstract class PresentationRequest { }
    public sealed class NotifyPresentation : PresentationRequest { public string Message { get; set; } = ""; public NoticeLevel Level { get; set; } }
    public sealed class StatusPresentation : PresentationRequest { public string Key { get; set; } = ""; public string? Text { get; set; } }
    public sealed class OpenUrlPresentation : PresentationRequest { public string Url { get; set; } = ""; public string? Instructions { get; set; } }
    public sealed class EditorTextPresentation : PresentationRequest { public string Text { get; set; } = ""; }

    /// <summary>A slash command OMP accepts as a prompt (OMP <c>available_commands_update</c>).</summary>
    public sealed class SlashCommandView
    {
        /// <summary>Name without the leading slash.</summary>
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>Argument hint, e.g. "&lt;plan|scan&gt;".</summary>
        public string? Hint { get; set; }
        public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();
        /// <summary>builtin, skill, extension, ... as reported by OMP.</summary>
        public string? Source { get; set; }
    }

    public sealed class OmpServiceOptions
    {
        /// <summary>Resolved absolute executable path.</summary>
        public string Executable { get; set; } = "";
        /// <summary>Arguments appended after "--mode rpc-ui".</summary>
        public IReadOnlyList<string> ExtraArgs { get; set; } = Array.Empty<string>();
        public string Cwd { get; set; } = "";
        /// <summary>Environment overrides for the OMP process (null value removes a variable).</summary>
        public IReadOnlyDictionary<string, string?>? Environment { get; set; }
        public IOmpLogger Logger { get; set; } = null!;
        public bool AutoRestart { get; set; } = true;
        /// <summary>Tools this client provides to the agent; registered with every OMP process the service starts.</summary>
        public IHostTools? HostTools { get; set; }
    }

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

    /// <summary>What a host tool returns to the agent; <see cref="IsError"/> reports the text as a tool failure.</summary>
    public sealed class HostToolResult
    {
        private HostToolResult(string text, bool isError)
        {
            Content = text;
            IsError = isError;
        }

        public string Content { get; }
        public bool IsError { get; }

        public static HostToolResult Text(string text) => new HostToolResult(text, false);

        public static HostToolResult Error(string text) => new HostToolResult(text, true);
    }

    /// <summary>The tools a client provides to OMP's agent.</summary>
    public interface IHostTools
    {
        IReadOnlyList<HostToolDefinition> Definitions { get; }

        /// <summary>Runs <paramref name="name"/>; a thrown exception is reported to the agent as a tool error.</summary>
        System.Threading.Tasks.Task<HostToolResult> InvokeAsync(string name, JObject arguments, System.Threading.CancellationToken cancellationToken);
    }

    public sealed class StartOptions
    {
        public string? ResumeSessionFile { get; set; }
        public bool NewSession { get; set; }
    }

    /// <summary>One rate limit of a provider, as OMP's <c>/usage</c> reports it.</summary>
    public sealed class UsageLimit
    {
        /// <summary>What the limit covers, as "Claude 7 Day" or "5 hours".</summary>
        public string Name { get; set; } = "";
        /// <summary>The account the limit belongs to, as OMP names it.</summary>
        public string Account { get; set; } = "";
        /// <summary>The share of the limit used, 0–100.</summary>
        public double UsedPercent { get; set; }
        /// <summary>When the limit resets, as "in 5d"; null when OMP does not say.</summary>
        public string? Resets { get; set; }
        /// <summary>Whether the current session draws on this limit.</summary>
        public bool InUse { get; set; }
    }

    /// <summary>A configured provider and its rate limits.</summary>
    public sealed class ProviderUsage
    {
        public string Provider { get; set; } = "";
        public IReadOnlyList<UsageLimit> Limits { get; set; } = Array.Empty<UsageLimit>();
    }

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
}
