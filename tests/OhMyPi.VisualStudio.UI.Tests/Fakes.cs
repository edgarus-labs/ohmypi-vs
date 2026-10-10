using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.UI.Tests;

internal sealed class FakeService : IOmpService
{
    public string Cwd { get; set; } = "C:\\repo";

    public ConnectionStatus Connection { get; set; } = new ConnectionStatus { State = ConnectionState.Ready };

    public SessionView Session { get; set; } = new SessionView();

    public IReadOnlyList<TranscriptItem> Transcript { get; set; } = Array.Empty<TranscriptItem>();

    public IReadOnlyList<AgentView> Agents { get; set; } = Array.Empty<AgentView>();

    private IReadOnlyList<SlashCommandView> _commands = Array.Empty<SlashCommandView>();

    public IReadOnlyList<SlashCommandView> Commands
    {
        get
        {
            var value = _commands;
            var read = CommandsRead;
            CommandsRead = null;
            read?.Invoke();

            return value;
        }
        set => _commands = value;
    }

    /// <summary>Runs once, on the next read of <see cref="Commands"/>, after the returned value was taken (an update racing the read).</summary>
    public Action? CommandsRead { get; set; }

    public IReadOnlyDictionary<string, string> Statuses { get; set; } = new Dictionary<string, string>();

    public IReadOnlyList<InteractionRequest> PendingInteractions { get; set; } = Array.Empty<InteractionRequest>();

    public List<(string Text, PromptMode Mode, IReadOnlyList<PromptImage>? Images)> Prompts { get; } = new List<(string, PromptMode, IReadOnlyList<PromptImage>?)>();

    public List<(string Id, InteractionResponse Response)> Responses { get; } = new List<(string, InteractionResponse)>();

    public IReadOnlyList<SessionSummary> Sessions { get; set; } = Array.Empty<SessionSummary>();

    public int Aborts { get; private set; }

    /// <summary>When set, OMP refuses every prompt with this error (an Error outcome that was not admitted).</summary>
    public string? PromptRefusal { get; set; }

    public Exception? PromptError { get; set; }

    public Func<Task<PromptOutcome>>? PromptWith { get; set; }

    public Exception? RespondError { get; set; }

    public Exception? ListModelsError { get; set; }

    public IReadOnlyList<ModelView> Models { get; set; } = new[] { new ModelView { Provider = "p", Id = "m", Name = "Model M" } };

    public event EventHandler<ConnectionStatus>? ConnectionChanged;

    public event EventHandler<SessionView>? SessionChanged;

    public event EventHandler<TranscriptItem>? TranscriptItemChanged;

    public event EventHandler<IReadOnlyList<TranscriptItem>>? TranscriptReset;

    public event EventHandler<IReadOnlyList<AgentView>>? AgentsChanged;

    public event EventHandler<ToolExecutionEvent>? ToolExecution;

    public event EventHandler<InteractionRequest>? InteractionRequested;

    public event EventHandler<string>? InteractionCancelled;

    public event EventHandler<PresentationRequest>? Presentation;

    public event EventHandler<IReadOnlyList<SlashCommandView>>? CommandsChanged;

    /// <summary>Handlers currently attached to any of this service's events.</summary>
    public int HandlerCount => new Delegate?[] { ConnectionChanged, SessionChanged, TranscriptItemChanged, TranscriptReset, AgentsChanged, ToolExecution, InteractionRequested, InteractionCancelled, Presentation, CommandsChanged }
        .Sum(handler => handler?.GetInvocationList().Length ?? 0);

    public void RaiseConnection(ConnectionStatus status) => ConnectionChanged?.Invoke(this, status);

    public void RaiseSession(SessionView session) => SessionChanged?.Invoke(this, session);

    public void RaiseItem(TranscriptItem item) => TranscriptItemChanged?.Invoke(this, item);

    public void RaiseReset(IReadOnlyList<TranscriptItem> items) => TranscriptReset?.Invoke(this, items);

    public void RaiseAgents(IReadOnlyList<AgentView> agents) => AgentsChanged?.Invoke(this, agents);

    public void RaiseInteraction(InteractionRequest request) => InteractionRequested?.Invoke(this, request);

    public void RaiseInteractionCancelled(string id) => InteractionCancelled?.Invoke(this, id);

    public void RaisePresentation(PresentationRequest request) => Presentation?.Invoke(this, request);

    public void RaiseCommands(IReadOnlyList<SlashCommandView> commands)
    {
        _commands = commands;
        CommandsChanged?.Invoke(this, commands);
    }

    public Task StartAsync(StartOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RestartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<ModelView>> ListModelsAsync() => ListModelsError is not null
        ? Task.FromException<IReadOnlyList<ModelView>>(ListModelsError)
        : Task.FromResult(Models);

    public IReadOnlyList<ProviderUsage> Usage { get; set; } = Array.Empty<ProviderUsage>();

    public Exception? UsageError { get; set; }

    public int UsageRequests { get; private set; }

    public Task<IReadOnlyList<ProviderUsage>> GetUsageAsync()
    {
        UsageRequests++;

        return UsageError is not null ? Task.FromException<IReadOnlyList<ProviderUsage>>(UsageError) : Task.FromResult(Usage);
    }

    public List<(string Provider, string Id)> ModelSelections { get; } = new List<(string, string)>();

    public Task SetModelAsync(string provider, string modelId)
    {
        ModelSelections.Add((provider, modelId));

        return Task.CompletedTask;
    }

    public List<string> ThinkingLevels { get; } = new List<string>();

    public Task SetThinkingLevelAsync(string level)
    {
        ThinkingLevels.Add(level);

        return Task.CompletedTask;
    }

    public List<bool> FastModes { get; } = new List<bool>();

    public Task SetFastModeAsync(bool enabled)
    {
        FastModes.Add(enabled);

        return Task.CompletedTask;
    }

    public Task<PromptOutcome> PromptAsync(string text, PromptMode mode = PromptMode.Auto, IReadOnlyList<PromptImage>? images = null)
    {
        if (PromptError is not null)
        {
            return Task.FromException<PromptOutcome>(PromptError);
        }

        if (PromptWith is not null)
        {
            return PromptWith();
        }

        if (PromptRefusal is not null)
        {
            return Task.FromResult(new PromptOutcome { Status = PromptStatus.Error, Error = PromptRefusal, Admitted = false });
        }

        Prompts.Add((text, mode, images));

        return Task.FromResult(new PromptOutcome { Status = PromptStatus.Completed, SessionSettled = true, Admitted = true });
    }

    public Task AbortAsync()
    {
        Aborts++;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously initializes a new session.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task NewSessionAsync() => Task.CompletedTask;

    /// <summary>
    /// Gets the collection of switches.
    /// </summary>
    public List<string> Switches { get; } = new List<string>();

    /// <summary>
    /// Asynchronously switches the current session by adding the specified session file to the collection of active switches.
    /// </summary>
    /// <param name="sessionFile">The session file.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SwitchSessionAsync(string sessionFile)
    {
        Switches.Add(sessionFile);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously sets the name of the current session.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetSessionNameAsync(string name) => Task.CompletedTask;

    /// <summary>
    /// Gets or sets the sessions error.
    /// </summary>
    public Exception? SessionsError { get; set; }

    /// <summary>
    /// Asynchronously retrieves a read-only list of session summaries or throws a cached exception if the session state is in an error condition.
    /// </summary>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync() => SessionsError is not null
        ? Task.FromException<IReadOnlyList<SessionSummary>>(SessionsError)
        : Task.FromResult(Sessions);

    /// <summary>
    /// Records a response for a specific interaction identified by the provided identifier.
    /// </summary>
    /// <param name="id">The unique identifier.</param>
    /// <param name="response">The response.</param>
    public void RespondInteraction(string id, InteractionResponse response)
    {
        if (RespondError is not null)
        {
            throw RespondError;
        }

        Responses.Add((id, response));
    }

    /// <summary>
    /// Gets or sets a value indicating whether cancel result.
    /// </summary>
    public bool CancelResult { get; set; } = true;

    /// <summary>
    /// Gets the collection of cancels.
    /// </summary>
    public List<string> Cancels { get; } = new List<string>();

    /// <summary>
    /// Asynchronously cancels the agent specified by the identifier and returns the result of the operation.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>A task representing the asynchronous operation. The task result is true if successful; otherwise, false.</returns>
    public Task<bool> CancelAgentAsync(string agentId)
    {
        Cancels.Add(agentId);

        return Task.FromResult(CancelResult);
    }

    /// <summary>
    /// Gets the collection of steers.
    /// </summary>
    public List<(string Id, string Message)> Steers { get; } = new List<(string, string)>();

    /// <summary>
    /// Asynchronously queues a steering message for the specified agent by adding it to the internal steering collection.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SteerAgentAsync(string agentId, string message)
    {
        Steers.Add((agentId, message));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets or sets the collection of agent transcript.
    /// </summary>
    public IReadOnlyList<TranscriptItem> AgentTranscript { get; set; } = Array.Empty<TranscriptItem>();

    /// <summary>
    /// Gets or sets the agent transcript error.
    /// </summary>
    public Exception? AgentTranscriptError { get; set; }

    /// <summary>
    /// Asynchronously retrieves the transcript items associated with the specified agent identifier.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    public Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId) => AgentTranscriptError is not null
        ? Task.FromException<IReadOnlyList<TranscriptItem>>(AgentTranscriptError)
        : Task.FromResult(AgentTranscript);

    /// <summary>
    /// Releases the unmanaged resources used by the instance.
    /// </summary>
    public void Dispose() { }
}
