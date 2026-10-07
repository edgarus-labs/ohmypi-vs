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

    public Task SetFastModeAsync(bool enabled) => throw new NotSupportedException();

    public Task<PromptOutcome> PromptAsync(string text, PromptMode mode = PromptMode.Auto, IReadOnlyList<PromptImage>? images = null) => throw new NotSupportedException();

    public Task AbortAsync() => throw new NotSupportedException();

    public Task NewSessionAsync()
    {
        Interlocked.Increment(ref NewSessions);

        return Task.CompletedTask;
    }

    public Task SwitchSessionAsync(string sessionFile) => throw new NotSupportedException();

    public Task SetSessionNameAsync(string name) => throw new NotSupportedException();

    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync() => throw new NotSupportedException();

    public void RespondInteraction(string id, InteractionResponse response) => throw new NotSupportedException();

    public Task<bool> CancelAgentAsync(string agentId) => throw new NotSupportedException();

    public Task SteerAgentAsync(string agentId, string message) => throw new NotSupportedException();

    public Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId) => throw new NotSupportedException();
}
