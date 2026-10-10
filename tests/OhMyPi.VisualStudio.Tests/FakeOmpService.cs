using Omp.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Tests;

internal sealed class FakeOmpService : IOmpService
{
    public readonly List<(StartOptions? Options, TaskCompletionSource<bool> Done)> Starts = new();
    public readonly List<TaskCompletionSource<bool>> Restarts = new();
    public int Stops;
    public int NewSessions;
    public bool Disposed;
    /// <summary>Runs once at the start of the next <see cref="StartAsync"/>, before the state changes.</summary>
    public Action? BeforeStart;
    /// <summary>Runs on every read of <see cref="Connection"/>.</summary>
    public Action? OnConnectionRead;
    /// <summary>When set, <see cref="StopAsync"/> completes with this task.</summary>
    public Task? StopResult;

    private ConnectionStatus _connection = new ConnectionStatus { State = ConnectionState.Stopped };

    public string Cwd { get; set; } = @"D:\work\repo";

    public ConnectionStatus Connection
    {
        get
        {
            OnConnectionRead?.Invoke();

            return _connection;
        }
    }

    public SessionView Session { get; private set; } = new SessionView();

    public IReadOnlyList<TranscriptItem> Transcript => Array.Empty<TranscriptItem>();

    public IReadOnlyList<AgentView> Agents => Array.Empty<AgentView>();

    public IReadOnlyList<SlashCommandView> Commands => Array.Empty<SlashCommandView>();

    public IReadOnlyDictionary<string, string> Statuses => new Dictionary<string, string>();

    public IReadOnlyList<InteractionRequest> PendingInteractions => Array.Empty<InteractionRequest>();

    public event EventHandler<ConnectionStatus>? ConnectionChanged;

    public event EventHandler<SessionView>? SessionChanged;

#pragma warning disable CS0067

    public event EventHandler<TranscriptItem>? TranscriptItemChanged;

    public event EventHandler<IReadOnlyList<TranscriptItem>>? TranscriptReset;

    public event EventHandler<IReadOnlyList<AgentView>>? AgentsChanged;

    public event EventHandler<ToolExecutionEvent>? ToolExecution;

    public event EventHandler<InteractionRequest>? InteractionRequested;

    public event EventHandler<string>? InteractionCancelled;

    public event EventHandler<PresentationRequest>? Presentation;

    public event EventHandler<IReadOnlyList<SlashCommandView>>? CommandsChanged;

#pragma warning restore CS0067

    public void SetConnection(ConnectionState state, string? detail = null)
    {
        _connection = new ConnectionStatus { State = state, Detail = detail };
        ConnectionChanged?.Invoke(this, _connection);
    }

    public void SetSession(SessionView session)
    {
        Session = session;
        SessionChanged?.Invoke(this, session);
    }

    public void RaiseToolExecution(ToolExecutionEvent e) => ToolExecution?.Invoke(this, e);

    public Task StartAsync(StartOptions? options = null, CancellationToken cancellationToken = default)
    {
        var before = BeforeStart;
        BeforeStart = null;
        before?.Invoke();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Starts)
        {
            Starts.Add((options, done));
        }

        SetConnection(ConnectionState.Starting);

        return done.Task;
    }

    public Task RestartAsync()
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Restarts)
        {
            Restarts.Add(done);
        }

        return done.Task;
    }

    /// <summary>Like the real service, stopping supersedes a pending start or restart.</summary>
    public Task StopAsync()
    {
        Interlocked.Increment(ref Stops);
        lock (Starts)
        {
            foreach (var start in Starts)
            {
                start.Done.TrySetCanceled();
            }
        }

        lock (Restarts)
        {
            foreach (var restart in Restarts)
            {
                restart.TrySetCanceled();
            }
        }

        SetConnection(ConnectionState.Stopped);

        return StopResult ?? Task.CompletedTask;
    }

    public void Dispose() => Disposed = true;

    public Task<IReadOnlyList<ModelView>> ListModelsAsync() => throw new NotSupportedException();

    public Task<IReadOnlyList<ProviderUsage>> GetUsageAsync() => throw new NotSupportedException();

    public Task SetModelAsync(string provider, string modelId) => throw new NotSupportedException();

    public Task SetThinkingLevelAsync(string level) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously sets whether fast mode is enabled for the current operation.
    /// </summary>
    /// <param name="enabled">The enabled.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task SetFastModeAsync(bool enabled) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously processes a text prompt, optionally including images and a specified prompt mode, to produce a prompt outcome.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="images">The collection of images.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the prompt outcome.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task<PromptOutcome> PromptAsync(string text, PromptMode mode = PromptMode.Auto, IReadOnlyList<PromptImage>? images = null) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously aborts the current operation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task AbortAsync() => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously increments the count of new sessions.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task NewSessionAsync()
    {
        Interlocked.Increment(ref NewSessions);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously switches the current active session to the one specified by the session file path.
    /// </summary>
    /// <param name="sessionFile">The session file.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task SwitchSessionAsync(string sessionFile) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously updates the session name to the specified value.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task SetSessionNameAsync(string name) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously retrieves a read-only list of session summaries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync() => throw new NotSupportedException();

    /// <summary>
    /// Sends a response to a specific interaction identified by the provided identifier.
    /// </summary>
    /// <param name="id">The unique identifier.</param>
    /// <param name="response">The response.</param>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public void RespondInteraction(string id, InteractionResponse response) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously cancels the agent associated with the specified identifier.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>A task representing the asynchronous operation. The task result is true if successful; otherwise, false.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task<bool> CancelAgentAsync(string agentId) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously sends a steering message to a specific agent to guide its behavior or direction.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task SteerAgentAsync(string agentId, string message) => throw new NotSupportedException();

    /// <summary>
    /// Asynchronously retrieves a read-only list of transcript items associated with the specified agent identifier.
    /// </summary>
    /// <param name="agentId">The unique identifier of the agent.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the iread only list.</returns>
    /// <exception cref="NotSupportedException">Thrown when an error occurs during execution.</exception>
    public Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId) => throw new NotSupportedException();
}
