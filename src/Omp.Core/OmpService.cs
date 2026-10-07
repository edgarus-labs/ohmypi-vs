using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;
using Omp.Core.Processes;
using Omp.Core.Protocol;
using Omp.Core.Session;

namespace Omp.Core
{
    /// <summary>Restart/shutdown timing and process creation; production uses the defaults.</summary>
    internal sealed class OmpServiceTuning
    {
        /// <summary>Delay before each automatic restart; its length is the restart budget per window.</summary>
        public int[] RestartDelaysMs { get; set; } = { 500, 2000, 5000 };
        public int RestartWindowMs { get; set; } = 60_000;
        public int ShutdownGraceMs { get; set; } = 3000;
        public int ReadyTimeoutMs { get; set; } = 30_000;
        public int HistoryPageLimit { get; set; } = 256;

        /// <summary>Creates the OMP process; tests substitute an in-memory one.</summary>
        public Func<OmpProcessOptions, IOmpProcessHandle> Spawn { get; set; } = options => new OmpProcess(options);
    }

    /// <summary>
    /// Owns the OMP child process, the RPC client and the session/agent state that survives restarts.
    /// State changes are serialized by one lock and published as fresh snapshots. Events are raised while that lock is
    /// held, on the thread that caused the change (see <see cref="IOmpService"/>), so handlers must return quickly and
    /// never block.
    /// </summary>
    public sealed class OmpService : IOmpService
    {
        private const int SessionCommandTimeoutMs = 60_000;
        private const int ModelDiscoveryTimeoutMs = 120_000;
        /// <summary>OMP's per-turn effort selector (<c>ConfiguredThinkingLevel</c> "auto").</summary>
        private const string AutoThinking = "auto";
        /// <summary>
        /// OMP's wire <c>input</c> has no secret flag; the UI masks a request only when it carries one of these truthy
        /// fields. Every <c>input</c> answer is redacted from RPC traces regardless, since any of them may be a credential.
        /// </summary>
        private static readonly string[] SecretFlags = { "secret", "password", "masked" };

        private readonly OmpServiceOptions _options;
        private readonly OmpServiceTuning _tuning;
        private readonly IOmpLogger _logger;
        private readonly object _sync = new object();
        /// <summary>Lets one usage request run at a time, so its report cannot mix with another's.</summary>
        private readonly SemaphoreSlim _usageGate = new SemaphoreSlim(1, 1);
        /// <summary>Collects command output while a usage request runs instead of adding it to the transcript; guarded by <see cref="_sync"/>.</summary>
        private StringBuilder? _usageCapture;
        private readonly SessionStore _store;
        private readonly AgentRegistry _agents;
        /// <summary>Runs while the session is busy; the turn ends, and is summarized, when the session is idle again.</summary>
        private Stopwatch? _turnClock;
        /// <summary>Transcript position where the current turn began.</summary>
        private int _turnFrom;
        private bool _turnAborted;
        /// <summary>Host tool calls in flight, by OMP's request id, so OMP can cancel them.</summary>
        private readonly Dictionary<string, CancellationTokenSource> _hostCalls = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private IReadOnlyList<SlashCommandView> _commands = Array.Empty<SlashCommandView>();
        private readonly PromptTracker _tracker;
        /// <summary>Pending interaction ids and whether each answer is redacted from RPC traces.</summary>
        private readonly Dictionary<string, bool> _pendingInteractions = new Dictionary<string, bool>(StringComparer.Ordinal);
        /// <summary>The same pending interactions as requests, in arrival order.</summary>
        private readonly List<InteractionRequest> _pendingRequests = new List<InteractionRequest>();
        private readonly List<long> _restartTimes = new List<long>();

        private ConnectionStatus _connection = new ConnectionStatus { State = ConnectionState.Stopped };
        private IOmpProcessHandle? _process;
        private OmpRpcClient? _client;
        private OmpRpcClient? _launchingClient;
        /// <summary>Bumped by every launch and stop; a launch whose epoch changed was superseded.</summary>
        private int _epoch;
        private int _restartTicket;
        private bool _stopping;
        private bool _disposed;
        private Timer? _restartTimer;

        public OmpService(OmpServiceOptions options)
            : this(options, new OmpServiceTuning())
        {
        }

        internal OmpService(OmpServiceOptions options, OmpServiceTuning tuning)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = options.Logger ?? throw new ArgumentException("OmpServiceOptions.Logger is required", nameof(options));
            _tuning = tuning;
            _store = new SessionStore(_logger);
            _agents = new AgentRegistry();
            _tracker = new PromptTracker(OnPhase);
            _store.SessionChanged += session =>
            {
                Raise(SessionChanged, nameof(SessionChanged), session);
                UpdateMainAgent();
            };
            _store.ItemChanged += item => Raise(TranscriptItemChanged, nameof(TranscriptItemChanged), item);
            _store.Reset += items =>
            {
                _turnFrom = Math.Min(_turnFrom, items.Count);
                Raise(TranscriptReset, nameof(TranscriptReset), items);
            };
            _store.ToolExecution += e =>
            {
                Raise(ToolExecution, nameof(ToolExecution), e);
                UpdateMainAgent();
            };
            _agents.Changed += agents => Raise(AgentsChanged, nameof(AgentsChanged), agents);
        }

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

        public string Cwd => _options.Cwd;

        public ConnectionStatus Connection
        {
            get { lock (_sync) return _connection; }
        }

        public SessionView Session
        {
            get { lock (_sync) return _store.Session; }
        }

        public IReadOnlyList<TranscriptItem> Transcript
        {
            get { lock (_sync) return _store.Transcript.ToArray(); }
        }

        public IReadOnlyList<AgentView> Agents
        {
            get { lock (_sync) return _agents.Agents; }
        }

        public IReadOnlyList<SlashCommandView> Commands
        {
            get { lock (_sync) return _commands; }
        }

        public IReadOnlyList<InteractionRequest> PendingInteractions
        {
            get { lock (_sync) return _pendingRequests.ToArray(); }
        }

        public async Task StartAsync(StartOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(OmpService));
                if (_client != null || _launchingClient != null) throw new InvalidOperationException("OMP is already running");
                _stopping = false;
                _restartTimes.Clear();
                CancelRestartTimer();
            }
            using (cancellationToken.Register(() => _ = StopAsync().ContinueWith(t => _logger.Error("Stopping a cancelled OMP start failed", t.Exception), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default)))
            {
                try
                {
                    await LaunchAsync(options ?? new StartOptions(), ConnectionState.Starting, null).ConfigureAwait(false);
                }
                catch (OmpSupersededException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    lock (_sync)
                    {
                        if (!_stopping) SetConnection(ConnectionState.Failed, error.Message);
                    }
                    throw;
                }
            }
        }

        public async Task RestartAsync()
        {
            var ticket = Interlocked.Increment(ref _restartTicket);
            string? sessionFile;
            lock (_sync) sessionFile = _store.Session.SessionFile;
            _logger.Info("Restarting OMP");
            await StopAsync().ConfigureAwait(false);
            if (ticket != Volatile.Read(ref _restartTicket)) throw new OmpSupersededException();
            await StartAsync(new StartOptions { ResumeSessionFile = sessionFile }).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            IOmpProcessHandle? process;
            OmpRpcClient? client;
            int epoch;
            lock (_sync)
            {
                _stopping = true;
                epoch = ++_epoch;
                CancelRestartTimer();
                process = _process;
                client = _client;
                _process = null;
                _client = null;
                _launchingClient = null;
                if (client != null) EndSession("OMP was stopped");
            }
            if (process != null)
            {
                try
                {
                    await process.ShutdownAsync(_tuning.ShutdownGraceMs).ConfigureAwait(false);
                    _logger.Info($"OMP stopped (pid {process.Pid})");
                }
                catch (Exception error)
                {
                    lock (_sync)
                    {
                        if (epoch == _epoch) SetConnection(ConnectionState.Failed, error.Message);
                    }
                    throw;
                }
                finally
                {
                    client?.Dispose();
                }
            }
            lock (_sync)
            {
                if (epoch == _epoch && _connection.State != ConnectionState.Stopped) SetConnection(ConnectionState.Stopped, _connection.Detail);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _ = StopAsync().ContinueWith(t => _logger.Error("Failed to stop OMP", t.Exception), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        public async Task<IReadOnlyList<ModelView>> ListModelsAsync()
        {
            var result = await RequestAsync("get_available_models", null, ModelDiscoveryTimeoutMs).ConfigureAwait(false);
            return (Json.Array(result, "models") ?? new JArray()).OfType<JObject>().Select(ModelMapper.ToModelView).ToArray();
        }

        public async Task<IReadOnlyList<ProviderUsage>> GetUsageAsync()
        {
            await _usageGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (_sync) _usageCapture = new StringBuilder();
                string text;
                try
                {
                    await RequestAsync("prompt", new JObject { ["message"] = "/usage" }).ConfigureAwait(false);
                }
                finally
                {
                    lock (_sync)
                    {
                        text = _usageCapture!.ToString();
                        _usageCapture = null;
                    }
                }
                if (!UsageReport.IsReport(text)) throw new InvalidOperationException("OMP printed no usage report");
                return UsageReport.Parse(text);
            }
            finally
            {
                _usageGate.Release();
            }
        }

        public async Task SetModelAsync(string provider, string modelId)
        {
            var result = await RequestAsync("set_model", new JObject { ["provider"] = provider, ["modelId"] = modelId }, ModelDiscoveryTimeoutMs).ConfigureAwait(false);
            if (result is JObject model)
            {
                lock (_sync) _store.Update(v => v.Model = ModelMapper.ToModelView(model));
            }
            await RefreshThinkingLevelsAsync().ConfigureAwait(false);
        }

        public async Task SetThinkingLevelAsync(string level)
        {
            await RequestAsync("set_thinking_level", new JObject { ["level"] = level }).ConfigureAwait(false);
            lock (_sync)
            {
                _store.Update(v =>
                {
                    v.ThinkingSelector = level;
                    v.ThinkingResolved = null;
                    if (level != AutoThinking) v.ThinkingLevel = level;
                });
            }
        }

        public async Task SetFastModeAsync(bool enabled)
        {
            var result = await RequestAsync("set_fast_mode", new JObject { ["enabled"] = enabled }).ConfigureAwait(false);
            lock (_sync)
            {
                _store.Update(v =>
                {
                    v.FastModeEnabled = Json.Bool(result, "enabled");
                    v.FastModeActive = Json.Bool(result, "active");
                });
            }
        }

        public async Task<PromptOutcome> PromptAsync(string text, PromptMode mode = PromptMode.Auto, IReadOnlyList<PromptImage>? images = null) =>
            await SubmitPrompt(text, mode, images).Outcome.ConfigureAwait(false);

        /// <summary>
        /// Sends a prompt. <c>Accepted</c> completes with OMP's acknowledgement and faults with the
        /// <see cref="OmpRequestException"/> when OMP refuses the prompt; <c>Outcome</c> completes with the prompt.
        /// </summary>
        private (Task Accepted, Task<PromptOutcome> Outcome) SubmitPrompt(string text, PromptMode mode, IReadOnlyList<PromptImage>? images)
        {
            OmpRpcClient client;
            string id;
            string? echoId = null;
            Task<PromptOutcome> outcome;
            var parameters = new JObject { ["message"] = text };
            lock (_sync)
            {
                client = RequireClient();
                var busy = _tracker.Phase != SessionPhase.Idle;
                if (busy) parameters["streamingBehavior"] = mode == PromptMode.FollowUp ? "followUp" : "steer";
                id = client.NextId();
                _store.Update(v => v.LastError = null);
                if (!busy) echoId = _store.AddPendingUser(text, images?.Count ?? 0);
                outcome = _tracker.SubmitAsync(id);
            }
            if (images != null && images.Count > 0)
                parameters["images"] = new JArray(images.Select(image => new JObject { ["type"] = "image", ["data"] = image.Data, ["mimeType"] = image.MimeType }));
            var accepted = client.RequestAsync("prompt", parameters, timeoutMs: 0, id: id);
            _ = accepted.ContinueWith(ack =>
            {
                try
                {
                    lock (_sync)
                    {
                        if (ack.Status == TaskStatus.RanToCompletion) _tracker.Acknowledged(id, Json.Bool(ack.Result, "agentInvoked"));
                        else _tracker.Rejected(id, ack.Exception?.InnerException?.Message ?? "prompt failed");
                    }
                }
                catch (Exception error)
                {
                    _logger.Error($"Recording OMP's answer to prompt {id} failed", error);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return (accepted, FinishPromptAsync(outcome, echoId));
        }

        /// <summary>
        /// Settles the echo row once the prompt completes: kept when OMP admitted the prompt (OMP recorded it), withdrawn
        /// when OMP refused it. An error outcome always leaves an error line, even when OMP gave no message.
        /// </summary>
        private async Task<PromptOutcome> FinishPromptAsync(Task<PromptOutcome> outcome, string? echoId)
        {
            var result = await outcome.ConfigureAwait(false);
            lock (_sync)
            {
                if (echoId != null)
                {
                    if (result.Admitted) _store.ForgetPendingUser(echoId);
                    else _store.RemovePendingUser(echoId);
                }
                if (result.Status == PromptStatus.Error) _store.Update(v => v.LastError = result.Error ?? "The prompt failed; OMP gave no reason");
            }
            return result;
        }

        /// <summary>Stops the current run and cancels every running or pending subagent, so background work cannot keep the session busy.</summary>
        public async Task AbortAsync()
        {
            string[] subagents;
            lock (_sync)
            {
                if (_tracker.Phase != SessionPhase.Idle) _turnAborted = true;
                _tracker.AbortRequested();
                subagents = _agents.Agents
                    .Where(a => a.Id != "main" && (a.Status == AgentStatus.Running || a.Status == AgentStatus.Pending))
                    .Select(a => a.Id)
                    .ToArray();
            }
            await RequestAsync("abort").ConfigureAwait(false);
            await Task.WhenAll(subagents.Select(id => RequestAsync("cancel_subagent", new JObject { ["subagentId"] = id }))).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes the phase and closes the turn: a turn spans from leaving idle to settling again, so runs OMP starts
        /// on its own (steering, follow-ups, background work) belong to the turn that is summarized at its end.
        /// </summary>
        private void OnPhase(SessionPhase phase)
        {
            _store.Update(v => v.Phase = phase);
            if (phase == SessionPhase.Aborting) _turnAborted = true;
            if (phase != SessionPhase.Idle)
            {
                if (_turnClock != null) return;
                _turnClock = Stopwatch.StartNew();
                _turnFrom = _store.Transcript.Count;
                _turnAborted = false;
                return;
            }
            if (_turnClock == null) return;
            _store.AddTurnSummary(_turnFrom, _turnClock.ElapsedMilliseconds, _turnAborted);
            _turnClock = null;
        }

        public async Task NewSessionAsync()
        {
            var result = await RequestAsync("new_session", new JObject(), SessionCommandTimeoutMs).ConfigureAwait(false);
            if (Json.Bool(result, "cancelled") == true) throw new InvalidOperationException("Creating a new session was cancelled by an OMP extension");
            await ReloadSessionAsync().ConfigureAwait(false);
        }

        public async Task SwitchSessionAsync(string sessionFile)
        {
            var result = await RequestAsync("switch_session", new JObject { ["sessionPath"] = sessionFile }, SessionCommandTimeoutMs).ConfigureAwait(false);
            if (Json.Bool(result, "cancelled") == true) throw new InvalidOperationException("Switching sessions was cancelled by an OMP extension");
            await ReloadSessionAsync().ConfigureAwait(false);
        }

        public async Task SetSessionNameAsync(string name)
        {
            await RequestAsync("set_session_name", new JObject { ["name"] = name }).ConfigureAwait(false);
            lock (_sync) _store.Update(v => v.SessionName = name);
        }

        public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync()
        {
            string? sessionFile;
            lock (_sync) sessionFile = _store.Session.SessionFile;
            var dir = string.IsNullOrEmpty(sessionFile) ? null : Path.GetDirectoryName(sessionFile);
            return dir == null ? Array.Empty<SessionSummary>() : await SessionLister.ListAsync(dir, _logger).ConfigureAwait(false);
        }

        /// <summary>
        /// Answer a pending interaction. Unknown or withdrawn ids are ignored; throws when the answer cannot be
        /// delivered, and the interaction then stays pending. An answer the pipe fails to deliver after accepting it
        /// is reported as an error notice in the transcript.
        /// </summary>
        public void RespondInteraction(string id, InteractionResponse response)
        {
            OmpRpcClient? client;
            bool secret;
            lock (_sync)
            {
                if (!_pendingInteractions.TryGetValue(id, out secret))
                {
                    _logger.Debug($"Response to an unknown or withdrawn OMP interaction {id} ignored");
                    return;
                }
                client = _client;
            }
            if (client == null) throw new InvalidOperationException($"Cannot answer OMP interaction {id}: OMP is not running");
            var frame = new JObject { ["type"] = "extension_ui_response", ["id"] = id };
            switch (response)
            {
                case ValueResponse value:
                    frame["value"] = value.Value;
                    break;
                case ConfirmedResponse confirmed:
                    frame["confirmed"] = confirmed.Confirmed;
                    break;
                case CancelledResponse _:
                    frame["cancelled"] = true;
                    break;
                case AnswersResponse answers:
                    frame["answers"] = new JArray(answers.Answers.Select(answer =>
                    {
                        var item = new JObject { ["id"] = answer.Id, ["selectedOptions"] = new JArray(answer.SelectedOptions) };
                        if (answer.CustomInput != null) item["customInput"] = answer.CustomInput;
                        return item;
                    }));
                    break;
                default:
                    throw new ArgumentException($"Unsupported interaction response {response.GetType().Name}", nameof(response));
            }
            client.SendUiResponse(frame, secret, error => ReportUndelivered(client, id, error));
            lock (_sync) RemovePending(id);
        }

        public async Task<bool> CancelAgentAsync(string agentId)
        {
            if (agentId == "main")
            {
                await AbortAsync().ConfigureAwait(false);
                return true;
            }
            var result = await RequestAsync("cancel_subagent", new JObject { ["subagentId"] = agentId }).ConfigureAwait(false);
            return Json.Bool(result, "cancelled") == true;
        }

        public async Task SteerAgentAsync(string agentId, string message)
        {
            if (agentId == "main")
            {
                var (accepted, outcome) = SubmitPrompt(message, PromptMode.Auto, null);
                _ = outcome.ContinueWith(t => _logger.Error("Steering the main agent failed", t.Exception), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                await accepted.ConfigureAwait(false);
                return;
            }
            await RequestAsync("steer_subagent", new JObject { ["subagentId"] = agentId, ["message"] = message }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<TranscriptItem>> GetAgentTranscriptAsync(string agentId)
        {
            if (agentId == "main") return Transcript;
            var result = await RequestAsync("get_subagent_messages", new JObject { ["subagentId"] = agentId }).ConfigureAwait(false);
            return History.ToItems(Json.Array(result, "messages"));
        }

        private static IReadOnlyList<SlashCommandView> ParseCommands(JArray commands)
        {
            var result = new List<SlashCommandView>(commands.Count);
            foreach (var command in commands)
            {
                var name = Json.Str(command, "name");
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new SlashCommandView
                {
                    Name = name!,
                    Description = Json.Str(command, "description"),
                    Hint = Json.Str(Json.Get(command, "input"), "hint"),
                    Aliases = (Json.Array(command, "aliases") ?? new JArray()).Select(Json.Str).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).ToArray(),
                    Source = Json.Str(command, "source"),
                });
            }
            return result;
        }

        private void Raise<T>(EventHandler<T>? handler, string name, T value)
        {
            if (!_disposed) Listeners.Raise(handler, name, this, value, _logger);
        }

        private void SetConnection(ConnectionState state, string? detail, int? pid = null, int? protocolVersion = null)
        {
            _connection = new ConnectionStatus { State = state, Detail = detail, Pid = pid, ProtocolVersion = protocolVersion };
            Raise(ConnectionChanged, nameof(ConnectionChanged), _connection);
        }

        private OmpRpcClient RequireClient()
        {
            if (_client == null || _connection.State != ConnectionState.Ready) throw new InvalidOperationException($"OMP is not running ({_connection.State.ToString().ToLowerInvariant()})");
            return _client;
        }

        private Task<JToken?> RequestAsync(string type, JObject? parameters = null, int? timeoutMs = null)
        {
            OmpRpcClient client;
            lock (_sync) client = RequireClient();
            return client.RequestAsync(type, parameters, timeoutMs);
        }

        private void CancelRestartTimer()
        {
            _restartTimer?.Dispose();
            _restartTimer = null;
        }

        /// <summary>The session's process went away: open prompts fail and pending interactions are withdrawn.</summary>
        /// <summary>Runs a host tool off OMP's reader thread and answers OMP; a failure or cancellation answers as a tool error.</summary>
        private void OnHostToolCall(OmpRpcClient client, IHostTools tools, JObject frame)
        {
            var id = Json.Str(frame, "id");
            if (id == null) return;
            var name = Json.Str(frame, "toolName") ?? "";
            var arguments = frame["arguments"] as JObject ?? new JObject();
            var cancellation = new CancellationTokenSource();
            _hostCalls[id] = cancellation;
            _ = Task.Run(async () =>
            {
                HostToolResult result;
                try
                {
                    result = await tools.InvokeAsync(name, arguments, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    result = HostToolResult.Error($"{name} was cancelled");
                }
                catch (Exception error)
                {
                    _logger.Warn($"Host tool {name} failed: {error.Message}");
                    result = HostToolResult.Error(error.Message);
                }
                lock (_sync) _hostCalls.Remove(id);
                cancellation.Dispose();
                client.SendHostToolResult(id, result);
            });
        }

        private void EndSession(string detail)
        {
            foreach (var call in _hostCalls.Values) call.Cancel();
            _tracker.Terminate(detail);
            _store.Update(v => v.IsCompacting = false);
            foreach (var id in _pendingInteractions.Keys.ToList()) Raise(InteractionCancelled, nameof(InteractionCancelled), id);
            _pendingInteractions.Clear();
            _pendingRequests.Clear();
        }

        /// <param name="restartTimer">The restart timer that fired, for an automatic restart; it must still be the current one.</param>
        private async Task LaunchAsync(StartOptions binding, ConnectionState state, string? detail, Timer? restartTimer = null)
        {
            IOmpProcessHandle process;
            OmpRpcClient client;
            int epoch;
            lock (_sync)
            {
                if (_stopping || _disposed || (restartTimer != null && !ReferenceEquals(restartTimer, _restartTimer))) throw new OmpSupersededException();
                if (restartTimer != null) CancelRestartTimer();
                epoch = ++_epoch;
                SetConnection(state, detail);
                if (epoch != _epoch) throw new OmpSupersededException();
                _logger.Info($"Starting OMP: {_options.Executable} --mode rpc-ui {string.Join(" ", _options.ExtraArgs)} (cwd {_options.Cwd})".TrimEnd());
                process = _tuning.Spawn(new OmpProcessOptions
                {
                    Executable = _options.Executable,
                    Args = _options.ExtraArgs,
                    Cwd = _options.Cwd,
                    Environment = _options.Environment,
                    Logger = _logger,
                });
                client = new OmpRpcClient(process, _logger, _tuning.ReadyTimeoutMs);
                _process = process;
                _client = client;
                _launchingClient = client;
                Wire(client);
            }
            var notices = new List<(NoticeLevel Level, string Text)>();
            try
            {
                process.Start();
                await client.StartAsync().ConfigureAwait(false);
                Check(epoch);
                _logger.Info($"OMP RPC connected (pid {process.Pid}, protocol v{client.ProtocolVersion})");
                await client.RequestAsync("set_subagent_subscription", new JObject { ["level"] = "progress" }).ConfigureAwait(false);
                Check(epoch);
                await ConfigureOptionalAsync(client).ConfigureAwait(false);
                Check(epoch);
                await BindSessionAsync(client, binding, notices).ConfigureAwait(false);
                Check(epoch);
                await LoadSessionAsync(client, epoch).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                bool superseded;
                lock (_sync)
                {
                    superseded = epoch != _epoch;
                    if (ReferenceEquals(_client, client))
                    {
                        _client = null;
                        _process = null;
                        EndSession(error.Message);
                    }
                    if (ReferenceEquals(_launchingClient, client)) _launchingClient = null;
                }
                client.Dispose();
                try
                {
                    await process.ShutdownAsync(_tuning.ShutdownGraceMs).ConfigureAwait(false);
                }
                catch (Exception shutdownError)
                {
                    _logger.Error("Failed to shut down OMP after a failed start", shutdownError);
                }
                if (error is OmpSupersededException) throw;
                if (superseded) throw new OmpSupersededException(error);
                throw;
            }
            lock (_sync)
            {
                if (epoch != _epoch) throw new OmpSupersededException();
                _launchingClient = null;
                foreach (var (level, text) in notices) _store.AddNotice(level, text);
                SetConnection(ConnectionState.Ready, null, process.Pid, client.ProtocolVersion);
            }
            _logger.Info($"OMP ready (pid {process.Pid}, protocol v{client.ProtocolVersion}, session {Session.SessionFile ?? "without a file"})");
        }

        private void Check(int epoch)
        {
            if (Volatile.Read(ref _epoch) != epoch) throw new OmpSupersededException();
        }

        private async Task ConfigureOptionalAsync(OmpRpcClient client)
        {
            var hostTools = _options.HostTools?.Definitions ?? Array.Empty<HostToolDefinition>();
            if (hostTools.Count > 0)
            {
                try
                {
                    var tools = new JArray(hostTools.Select(tool => new JObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.Parameters }));
                    await client.RequestAsync("set_host_tools", new JObject { ["tools"] = tools }).ConfigureAwait(false);
                }
                catch (OmpRequestException error) when (error.Code != "closed")
                {
                    _logger.Warn($"OMP did not accept the Visual Studio tools ({error.Code ?? "no code"}): {error.Message}");
                }
            }
            try
            {
                await client.RequestAsync("set_ask_dialog", new JObject { ["enabled"] = true }).ConfigureAwait(false);
            }
            catch (OmpRequestException error) when (error.Code != "closed")
            {
                _logger.Info($"OMP ask dialog unavailable; using select prompts: {error.Message}");
            }
            try
            {
                var filter = await client.RequestAsync("set_event_filter", new JObject { ["events"] = null, ["messageUpdates"] = "delta" }).ConfigureAwait(false);
                if (Json.Str(filter, "messageUpdates") != "delta") _logger.Debug("OMP does not project message updates; using full snapshots");
            }
            catch (OmpRequestException error) when (error.Code != "closed")
            {
                _logger.Warn($"OMP event filter could not be set ({error.Code ?? "no code"}): {error.Message}; OMP sends every event in full");
            }
        }

        private async Task BindSessionAsync(OmpRpcClient client, StartOptions binding, List<(NoticeLevel, string)> notices)
        {
            var file = binding.ResumeSessionFile;
            if (!string.IsNullOrEmpty(file))
            {
                if (File.Exists(file))
                {
                    var result = await client.RequestAsync("switch_session", new JObject { ["sessionPath"] = file }, SessionCommandTimeoutMs).ConfigureAwait(false);
                    if (Json.Bool(result, "cancelled") == true)
                    {
                        var message = $"Resuming {file} was cancelled by an OMP extension";
                        _logger.Warn(message);
                        notices.Add((NoticeLevel.Warning, message));
                    }
                    else
                    {
                        _logger.Info($"Resumed OMP session {file}");
                    }
                    return;
                }
                var missing = $"Session file {file} no longer exists; starting a new session";
                _logger.Warn(missing);
                notices.Add((NoticeLevel.Warning, missing));
                await client.RequestAsync("new_session", new JObject(), SessionCommandTimeoutMs).ConfigureAwait(false);
                return;
            }
            if (binding.NewSession)
            {
                await client.RequestAsync("new_session", new JObject(), SessionCommandTimeoutMs).ConfigureAwait(false);
                _logger.Info("Started a new OMP session");
            }
        }

        private async Task ReloadSessionAsync()
        {
            OmpRpcClient client;
            int epoch;
            lock (_sync)
            {
                client = RequireClient();
                epoch = _epoch;
            }
            await LoadSessionAsync(client, epoch).ConfigureAwait(false);
            _logger.Info($"OMP session {Session.SessionFile ?? "without a file"} loaded");
        }

        /// <summary>
        /// Snapshot state, thinking levels, effort selector, history and subagents after (re)binding a session. Events
        /// applied while the snapshots were on their way win over the older snapshot values.
        /// </summary>
        private async Task LoadSessionAsync(OmpRpcClient client, int epoch)
        {
            long stateAt;
            lock (_sync) stateAt = _store.Version;
            var state = await client.RequestAsync("get_state").ConfigureAwait(false) as JObject ?? new JObject();
            var levels = Json.Strings(Json.Get(await client.RequestAsync("get_available_thinking_levels").ConfigureAwait(false), "levels"));
            var selector = await JournaledThinkingSelectorAsync(client, Json.Str(state, "thinkingLevel")).ConfigureAwait(false);
            var messages = await LoadMessagesAsync(client).ConfigureAwait(false);
            long agentsAt;
            lock (_sync) agentsAt = _agents.Version;
            var subagents = Json.Array(await client.RequestAsync("get_subagents").ConfigureAwait(false), "subagents");
            lock (_sync)
            {
                if (epoch != _epoch) throw new OmpSupersededException();
                _store.ApplyState(state, stateAt);
                var selectable = SelectableLevels(levels);
                _store.Update(v =>
                {
                    v.AvailableThinkingLevels = selectable;
                    v.ThinkingSelector = selector;
                });
                _store.ResetHistory(messages);
                _agents.ApplySnapshot(agentsAt, subagents);
            }
        }

        private async Task<JArray> LoadMessagesAsync(OmpRpcClient client)
        {
            if (client.ProtocolVersion == 2)
            {
                try
                {
                    var messages = new JArray();
                    string? cursor = null;
                    double? total = null;
                    do
                    {
                        var parameters = new JObject { ["limit"] = _tuning.HistoryPageLimit };
                        if (cursor != null) parameters["cursor"] = cursor;
                        var page = await client.RequestAsync("get_messages_page", parameters, SessionCommandTimeoutMs).ConfigureAwait(false);
                        var pageTotal = Json.Num(page, "totalMessages");
                        if (total != null && pageTotal != total)
                            throw new OmpRequestException("Message pages changed while loading", "get_messages_page", "stale_cursor");
                        total = pageTotal;
                        foreach (var message in Json.Array(page, "messages") ?? new JArray()) messages.Add(message);
                        cursor = Json.Str(page, "nextCursor");
                    }
                    while (!string.IsNullOrEmpty(cursor));
                    return messages;
                }
                catch (OmpRequestException error) when (error.Code != "closed" && error.Code != "timeout")
                {
                    _logger.Debug($"Paged history unavailable ({error.Message}); loading it in one request");
                }
            }
            return Json.Array(await client.RequestAsync("get_messages", null, SessionCommandTimeoutMs).ConfigureAwait(false), "messages") ?? new JArray();
        }

        /// <summary>
        /// <c>get_state</c> reports only the effective level, so <c>auto</c> is recovered from the newest journaled
        /// <c>thinking_level_change</c> entry (<c>configured</c>, else its level).
        /// </summary>
        private async Task<string?> JournaledThinkingSelectorAsync(OmpRpcClient client, string? stateLevel)
        {
            try
            {
                var entries = Json.Array(await client.RequestAsync("get_entries", null, SessionCommandTimeoutMs).ConfigureAwait(false), "entries") ?? new JArray();
                var latest = entries.OfType<JObject>().LastOrDefault(entry => Json.Str(entry, "type") == "thinking_level_change");
                return Json.Str(latest, "configured") ?? Json.Str(latest, "thinkingLevel") ?? stateLevel;
            }
            catch (OmpRequestException error) when (error.Code != "closed")
            {
                _logger.Warn($"Reading the effort selector from the session journal failed; showing the effective level: {error.Message}");
                return stateLevel;
            }
        }

        private async Task RefreshThinkingLevelsAsync()
        {
            var levels = Json.Strings(Json.Get(await RequestAsync("get_available_thinking_levels").ConfigureAwait(false), "levels"));
            lock (_sync)
            {
                var selectable = SelectableLevels(levels);
                _store.Update(v => v.AvailableThinkingLevels = selectable);
            }
        }

        /// <summary>OMP's discovery omits its <c>auto</c> selector; OMP offers it (after <c>off</c>) for every reasoning model.</summary>
        private IReadOnlyList<string> SelectableLevels(IReadOnlyList<string> levels)
        {
            if (_store.Session.Model?.Reasoning != true || levels.Count == 0 || levels.Contains(AutoThinking)) return levels;
            var afterOff = levels.ToList().IndexOf("off") + 1;
            return levels.Take(afterOff).Concat(new[] { AutoThinking }).Concat(levels.Skip(afterOff)).ToArray();
        }

        /// <summary>
        /// Background refresh after events that change state without carrying it. Called under the lock; fields that
        /// events change after this call keep the event's value.
        /// </summary>
        private void RefreshState(bool withThinkingLevels)
        {
            var requestedAt = _store.Version;
            _ = Task.Run(async () =>
            {
                try
                {
                    var state = await RequestAsync("get_state").ConfigureAwait(false) as JObject;
                    if (state != null)
                    {
                        lock (_sync) _store.ApplyState(state, requestedAt);
                    }
                    if (withThinkingLevels) await RefreshThinkingLevelsAsync().ConfigureAwait(false);
                }
                catch (OmpRequestException error) when (error.Code == "closed")
                {
                }
                catch (Exception error) when (error is OmpRequestException || error is InvalidOperationException)
                {
                    _logger.Warn($"Refreshing OMP state failed: {error.Message}", error);
                }
                catch (Exception error)
                {
                    _logger.Error("Refreshing OMP state failed", error);
                }
            });
        }

        private void UpdateMainAgent()
        {
            var session = _store.Session;
            _agents.SetMain(session.Phase, session.Model == null ? null : $"{session.Model.Provider}/{session.Model.Id}", _store.RunningTool);
        }

        private void Wire(OmpRpcClient client)
        {
            void Current(Action action)
            {
                lock (_sync)
                {
                    if (ReferenceEquals(client, _client)) action();
                }
            }

            client.SessionEvent += e => Current(() => OnSessionEvent(e));
            if (_options.HostTools != null)
            {
                client.HostToolCall += frame => Current(() => OnHostToolCall(client, _options.HostTools, frame));
                client.HostToolCancel += frame => Current(() =>
                {
                    var target = Json.Str(frame, "targetId");
                    if (target != null && _hostCalls.TryGetValue(target, out var call)) call.Cancel();
                });
            }
            client.PromptResult += frame => Current(() =>
            {
                if (Json.Str(frame, "status") == "aborted") _turnAborted = true;
                _tracker.PromptResult(frame);
                var error = Json.Str(Json.Get(frame, "error"), "message");
                if (Json.Str(frame, "status") == "error" && error != null) _store.Update(v => v.LastError = error);
            });
            client.SessionSettled += () => Current(_tracker.SessionSettled);
            client.CommandOutput += frame => Current(() =>
            {
                var text = Json.Str(frame, "text") ?? "";
                if (_usageCapture != null) _usageCapture.Append(text).Append('\n');
                else _store.AddCommandOutput(text);
            });
            client.UiRequest += request => Current(() => OnUiRequest(client, request));
            client.SubagentFrame += frame => Current(() => _agents.ApplyFrame(frame));
            client.CommandsUpdate += frame =>
            {
                lock (_sync)
                {
                    if (!ReferenceEquals(client, _client) && !ReferenceEquals(client, _launchingClient)) return;
                    if (!(frame["commands"] is JArray commands))
                    {
                        _logger.Warn($"OMP available_commands_update without a command list ignored; keeping {_commands.Count} commands");
                        return;
                    }
                    _commands = ParseCommands(commands);
                    Raise(CommandsChanged, nameof(CommandsChanged), _commands);
                }
            };
            client.SessionInfoUpdate += frame => Current(() =>
            {
                if (frame.ContainsKey("title")) _store.Update(v => v.SessionName = Json.Str(frame, "title"));
            });
            client.ConfigUpdate += _ => Current(() => RefreshState(true));
            client.Closed += close => OnClose(client, close);
        }

        private void OnSessionEvent(JObject e)
        {
            _store.ApplyEvent(e);
            switch (Json.Str(e, "type"))
            {
                case "agent_start":
                    _tracker.AgentStart();
                    return;
                case "agent_end":
                    _tracker.AgentEnd(Json.Bool(e, "yielded") ?? Json.Bool(e, "isTerminal") != false);
                    RefreshState(false);
                    return;
                case "model_changed":
                    RefreshState(true);
                    return;
            }
        }

        private void OnUiRequest(OmpRpcClient client, JObject request)
        {
            var id = Json.Str(request, "id") ?? "";
            var method = Json.Str(request, "method");
            var timeout = Json.Num(request, "timeout");
            var timeoutMs = timeout.HasValue ? (int?)timeout.Value : null;
            switch (method)
            {
                case "confirm":
                    Interact(new ConfirmRequest { Id = id, Title = Json.Str(request, "title") ?? "", Message = Json.Str(request, "message"), TimeoutMs = timeoutMs }, false);
                    return;
                case "select":
                {
                    if (!(request["options"] is JArray options) || options.Count == 0 || options.Any(o => Json.Str(o) == null))
                    {
                        CancelUnsupported(client, id, method);
                        return;
                    }
                    var details = request["optionDetails"] as JArray;
                    Interact(new SelectRequest
                    {
                        Id = id,
                        Title = Json.Str(request, "title") ?? "",
                        Options = options.Select((option, i) => new SelectOptionView
                        {
                            Label = Json.Str(option)!,
                            Description = details != null && i < details.Count && !string.IsNullOrEmpty(Json.Str(details[i], "description")) ? Json.Str(details[i], "description") : null,
                        }).ToArray(),
                        TimeoutMs = timeoutMs,
                    }, false);
                    return;
                }
                case "input":
                {
                    var secret = SecretFlags.Any(flag => Json.Truthy(request[flag]));
                    Interact(new InputRequest { Id = id, Title = Json.Str(request, "title") ?? "", Placeholder = Json.Str(request, "placeholder"), Secret = secret, TimeoutMs = timeoutMs }, true);
                    return;
                }
                case "editor":
                    Interact(new EditorRequest { Id = id, Title = Json.Str(request, "title") ?? "", Prefill = Json.Str(request, "prefill") }, false);
                    return;
                case "ask":
                {
                    if (!(request["questions"] is JArray questions) || questions.Count == 0)
                    {
                        CancelUnsupported(client, id, method);
                        return;
                    }
                    Interact(new AskRequest { Id = id, Questions = questions.OfType<JObject>().Select(AskQuestion).ToArray(), TimeoutMs = timeoutMs }, false);
                    return;
                }
                case "cancel":
                {
                    var target = Json.Str(request, "targetId");
                    if (target != null && RemovePending(target)) Raise(InteractionCancelled, nameof(InteractionCancelled), target);
                    return;
                }
                case "notify":
                    Raise(Presentation, nameof(Presentation), new NotifyPresentation { Message = Json.Str(request, "message") ?? "", Level = SessionStore.ParseLevel(Json.Str(request, "notifyType")) });
                    return;
                case "setStatus":
                    Raise(Presentation, nameof(Presentation), new StatusPresentation { Key = Json.Str(request, "statusKey") ?? "", Text = Json.Str(request, "statusText") });
                    return;
                case "open_url":
                    Raise(Presentation, nameof(Presentation), new OpenUrlPresentation { Url = Json.Str(request, "launchUrl") ?? Json.Str(request, "url") ?? "", Instructions = Json.Str(request, "instructions") });
                    return;
                case "set_editor_text":
                    Raise(Presentation, nameof(Presentation), new EditorTextPresentation { Text = Json.Str(request, "text") ?? "" });
                    return;
                case "setWidget":
                case "setTitle":
                    _logger.Debug($"OMP {method} request ignored");
                    return;
                default:
                    _logger.Debug($"Unsupported OMP UI method {method} ignored");
                    return;
            }
        }

        private static AskQuestionView AskQuestion(JObject question) => new AskQuestionView
        {
            Id = Json.Str(question, "id") ?? "",
            Question = Json.Str(question, "question") ?? "",
            Header = Json.Str(question, "header"),
            Options = (question["options"] as JArray ?? new JArray()).OfType<JObject>().Select(option => new AskOptionView
            {
                Label = Json.Str(option, "label") ?? "",
                Description = Json.Str(option, "description"),
                Preview = Json.Str(option, "preview"),
            }).ToArray(),
            Multi = Json.Bool(question, "multi") == true,
            Recommended = Json.Num(question, "recommended") is double recommended ? (int?)recommended : null,
        };

        /// <summary>A request the host cannot present is answered as cancelled so OMP does not wait for it.</summary>
        private void CancelUnsupported(OmpRpcClient client, string id, string? method)
        {
            var unsupported = $"OMP {method} request {id} cannot be shown (missing options)";
            try
            {
                client.SendUiResponse(new JObject { ["type"] = "extension_ui_response", ["id"] = id, ["cancelled"] = true }, false, error => ReportUndelivered(client, id, error));
            }
            catch (Exception error) when (error is InvalidOperationException || error is IOException)
            {
                var failed = $"{unsupported}, and cancelling it failed: {error.Message}";
                _logger.Error(failed, error);
                _store.AddNotice(NoticeLevel.Error, failed);
                return;
            }
            var cancelled = $"{unsupported}; it was cancelled";
            _logger.Warn(cancelled);
            _store.AddNotice(NoticeLevel.Warning, cancelled);
        }

        /// <summary>The pipe failed an answer to interaction <paramref name="id"/> after accepting it, so OMP is still waiting for it.</summary>
        private void ReportUndelivered(OmpRpcClient client, string id, Exception error)
        {
            var message = $"OMP did not receive the answer to interaction {id}: {error.Message}";
            _logger.Warn(message, error);
            lock (_sync)
            {
                if (ReferenceEquals(client, _client)) _store.AddNotice(NoticeLevel.Error, message);
            }
        }

        /// <param name="traceSecret">The answer is redacted from RPC traces.</param>
        private void Interact(InteractionRequest request, bool traceSecret)
        {
            _pendingInteractions[request.Id] = traceSecret;
            _pendingRequests.RemoveAll(r => r.Id == request.Id);
            _pendingRequests.Add(request);
            Raise(InteractionRequested, nameof(InteractionRequested), request);
        }

        private bool RemovePending(string id)
        {
            _pendingRequests.RemoveAll(r => r.Id == id);
            return _pendingInteractions.Remove(id);
        }

        private void OnClose(OmpRpcClient client, TransportClose close)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(client, _client)) return;
                var detail = OmpRpcClient.DescribeClose(close);
                _process = null;
                _client = null;
                EndSession(detail);
                if (ReferenceEquals(client, _launchingClient))
                {
                    if (!_stopping) _logger.Warn(WithSuffix(detail, " during startup"));
                    return;
                }
                if (_stopping) return;

                var unexpected = WithSuffix(detail, " unexpectedly");
                _logger.Warn(unexpected);
                if (!_options.AutoRestart)
                {
                    _store.AddNotice(NoticeLevel.Error, unexpected);
                    SetConnection(ConnectionState.Failed, detail);
                    return;
                }
                ScheduleRestart(unexpected);
            }
        }

        /// <summary>Appends <paramref name="suffix"/> to the first line, ahead of any stderr tail.</summary>
        private static string WithSuffix(string detail, string suffix)
        {
            var newline = detail.IndexOf('\n');
            return newline < 0 ? detail + suffix : detail.Substring(0, newline) + suffix + detail.Substring(newline);
        }

        private void ScheduleRestart(string detail)
        {
            var now = Listeners.NowMs();
            _restartTimes.RemoveAll(time => now - time >= _tuning.RestartWindowMs);
            var attempt = _restartTimes.Count;
            var budget = _tuning.RestartDelaysMs.Length;
            if (attempt >= budget)
            {
                var gaveUp = WithSuffix(detail, $"; gave up after {attempt} restarts within {Math.Round(_tuning.RestartWindowMs / 1000d, MidpointRounding.AwayFromZero)}s");
                _logger.Error(gaveUp);
                _store.AddNotice(NoticeLevel.Error, gaveUp);
                SetConnection(ConnectionState.Failed, gaveUp);
                return;
            }
            _restartTimes.Add(now);
            var delay = _tuning.RestartDelaysMs[attempt];
            var sessionFile = _store.Session.SessionFile;
            SetConnection(ConnectionState.Restarting, WithSuffix(detail, $"; restart {attempt + 1}/{budget} in {delay} ms"));
            _logger.Info($"Restarting OMP in {delay} ms (attempt {attempt + 1}/{budget})");
            Timer? timer = null;
            timer = new Timer(_ => OnRestartDue(timer!, sessionFile, detail), null, Timeout.Infinite, Timeout.Infinite);
            _restartTimer = timer;
            timer.Change(delay, Timeout.Infinite);
        }

        private void OnRestartDue(Timer timer, string? sessionFile, string detail)
        {
            _ = LaunchAsync(new StartOptions { ResumeSessionFile = sessionFile }, ConnectionState.Restarting, detail, timer).ContinueWith(launch =>
            {
                if (launch.Status == TaskStatus.RanToCompletion)
                {
                    lock (_sync) _store.AddNotice(NoticeLevel.Error, WithSuffix(detail, "; restarted"));
                    return;
                }
                var error = launch.Exception?.InnerException;
                if (error is OmpSupersededException) return;
                _logger.Error("OMP restart failed", error);
                lock (_sync)
                {
                    if (!_stopping) ScheduleRestart($"OMP restart failed: {error?.Message}");
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
