using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Omp.Core;
using Omp.Core.Changes;

namespace OhMyPi.VisualStudio.UI.Tests
{
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
        public Task<IReadOnlyList<ModelView>> ListModelsAsync() => ListModelsError != null
            ? Task.FromException<IReadOnlyList<ModelView>>(ListModelsError)
            : Task.FromResult(Models);
        public IReadOnlyList<ProviderUsage> Usage { get; set; } = Array.Empty<ProviderUsage>();
        public Exception? UsageError { get; set; }
        public int UsageRequests { get; private set; }
        public Task<IReadOnlyList<ProviderUsage>> GetUsageAsync()
        {
            UsageRequests++;
            return UsageError != null ? Task.FromException<IReadOnlyList<ProviderUsage>>(UsageError) : Task.FromResult(Usage);
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
            if (PromptError != null) return Task.FromException<PromptOutcome>(PromptError);
            if (PromptWith != null) return PromptWith();
            if (PromptRefusal != null) return Task.FromResult(new PromptOutcome { Status = PromptStatus.Error, Error = PromptRefusal, Admitted = false });
            Prompts.Add((text, mode, images));
            return Task.FromResult(new PromptOutcome { Status = PromptStatus.Completed, SessionSettled = true, Admitted = true });
        }

        public Task AbortAsync()
        {
            Aborts++;
            return Task.CompletedTask;
        }

        public Task NewSessionAsync() => Task.CompletedTask;
        public List<string> Switches { get; } = new List<string>();
        public Task SwitchSessionAsync(string sessionFile)
        {
            Switches.Add(sessionFile);
            return Task.CompletedTask;
        }
        public Task SetSessionNameAsync(string name) => Task.CompletedTask;
        public Exception? SessionsError { get; set; }
        public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync() => SessionsError != null
            ? Task.FromException<IReadOnlyList<SessionSummary>>(SessionsError)
            : Task.FromResult(Sessions);
        public void RespondInteraction(string id, InteractionResponse response)
        {
            if (RespondError != null) throw RespondError;
            Responses.Add((id, response));
        }
        public bool CancelResult { get; set; } = true;
        public List<string> Cancels { get; } = new List<string>();
        public Task<bool> CancelAgentAsync(string agentId)
        {
            Cancels.Add(agentId);
            return Task.FromResult(CancelResult);
        }
        public List<(string Id, string Message)> Steers { get; } = new List<(string, string)>();
        public Task SteerAgentAsync(string agentId, string message)
        {
            Steers.Add((agentId, message));
            return Task.CompletedTask;
        }
        public IReadOnlyList<TranscriptItem> AgentTranscript { get; set; } = Array.Empty<TranscriptItem>();
        public Exception? AgentTranscriptError { get; set; }
        public Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId) => AgentTranscriptError != null
            ? Task.FromException<IReadOnlyList<TranscriptItem>>(AgentTranscriptError)
            : Task.FromResult(AgentTranscript);
        public void Dispose() { }
    }

    internal sealed class FakeHost : IOmpHost
    {
        public string? ActiveDocumentPath { get; private set; }
        public event EventHandler? ActiveDocumentChanged;
        private IReadOnlyList<TrackedChange> _changes = Array.Empty<TrackedChange>();

        public IReadOnlyList<TrackedChange> Changes
        {
            get => ChangesError != null ? throw ChangesError : _changes;
            set => _changes = value;
        }

        public Exception? ChangesError { get; set; }

        /// <summary>When set, <see cref="EnsureServiceAsync"/> completes only with this task.</summary>
        public TaskCompletionSource<bool>? EnsureGate { get; set; }
        public Exception? EnsureError { get; set; }

        /// <summary>Handlers currently attached to any of this host's events.</summary>
        public int HandlerCount => (ActiveDocumentChanged?.GetInvocationList().Length ?? 0) + (ChangesChanged?.GetInvocationList().Length ?? 0);
        public event EventHandler? ChangesChanged;
        public List<string> Diffs { get; } = new List<string>();
        public List<(string Path, int? Line)> Opened { get; } = new List<(string, int?)>();
        public OmpUnavailable? Unavailable { get; set; }
        public FakeModelPreferences Preferences { get; } = new FakeModelPreferences();
        public IModelPreferences ModelPreferences => Preferences;

        public void RaiseChanges() => ChangesChanged?.Invoke(this, EventArgs.Empty);

        public void SetActiveDocument(string? path)
        {
            ActiveDocumentPath = path;
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        public List<(string Path, string? RecordedBefore)> DiffRequests { get; } = new List<(string, string?)>();
        public List<(string Message, Exception Error)> Errors { get; } = new List<(string, Exception)>();
        public int EnsureCalls { get; private set; }

        public Task OpenDiffAsync(string path, string? recordedBefore = null)
        {
            Diffs.Add(path);
            DiffRequests.Add((path, recordedBefore));
            return Task.CompletedTask;
        }

        public Task EnsureServiceAsync()
        {
            EnsureCalls++;
            if (EnsureError != null) return Task.FromException(EnsureError);
            return EnsureGate?.Task ?? Task.CompletedTask;
        }

        public void LogError(string message, Exception error) => Errors.Add((message, error));

        public Task OpenFileAsync(string path, int? line = null)
        {
            Opened.Add((path, line));
            return Task.CompletedTask;
        }

        public void ShowLog() { }
        public void OpenSettings() { }
        public Task RestartAsync() => Task.CompletedTask;
    }

    /// <summary>A host that remembers closed items, as Visual Studio's host does.</summary>
    internal sealed class FakeDismissingHost : IOmpHost, IDismissals
    {
        private readonly FakeHost _host = new FakeHost();

        public HashSet<string> Dismissed { get; } = new HashSet<string>();
        public Exception? DismissError { get; set; }
        public List<(string Message, Exception Error)> Errors => _host.Errors;

        public bool IsDismissed(string key) => Dismissed.Contains(key);

        public void Dismiss(string key)
        {
            if (DismissError != null) throw DismissError;
            Dismissed.Add(key);
        }

        public string? ActiveDocumentPath => _host.ActiveDocumentPath;
        public event EventHandler? ActiveDocumentChanged { add => _host.ActiveDocumentChanged += value; remove => _host.ActiveDocumentChanged -= value; }
        public IReadOnlyList<TrackedChange> Changes => _host.Changes;
        public event EventHandler? ChangesChanged { add => _host.ChangesChanged += value; remove => _host.ChangesChanged -= value; }
        public OmpUnavailable? Unavailable => _host.Unavailable;
        public IModelPreferences ModelPreferences => _host.ModelPreferences;
        public Task OpenDiffAsync(string path, string? recordedBefore = null) => _host.OpenDiffAsync(path, recordedBefore);
        public Task EnsureServiceAsync() => _host.EnsureServiceAsync();
        public void LogError(string message, Exception error) => _host.LogError(message, error);
        public Task OpenFileAsync(string path, int? line = null) => _host.OpenFileAsync(path, line);
        public void ShowLog() => _host.ShowLog();
        public void OpenSettings() => _host.OpenSettings();
        public Task RestartAsync() => _host.RestartAsync();
    }

    internal sealed class FakeModelPreferences : IModelPreferences
    {
        private readonly List<ModelKey> _favorites = new List<ModelKey>();
        private readonly List<ModelKey> _recents = new List<ModelKey>();

        public IReadOnlyList<ModelKey> Favorites => _favorites.ToArray();
        public IReadOnlyList<ModelKey> Recents => _recents.ToArray();
        /// <summary>When set, saving a favorite or a pick throws it.</summary>
        public Exception? SaveError { get; set; }

        public void SetFavorite(ModelKey model, bool favorite)
        {
            if (SaveError != null) throw SaveError;
            _favorites.Remove(model);
            if (favorite) _favorites.Add(model);
        }

        public void RecordPicked(ModelKey model)
        {
            if (SaveError != null) throw SaveError;
            _recents.Remove(model);
            _recents.Insert(0, model);
        }
    }
}
