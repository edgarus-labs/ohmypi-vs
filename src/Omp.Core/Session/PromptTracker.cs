using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Session
{
    /// <summary>
    /// Prompt tickets and the session phase machine documented on <see cref="SessionPhase"/>. A prompt completes
    /// only through its own <c>prompt_result</c>, local completion (<c>agentInvoked: false</c>), a command failure,
    /// or termination. Not thread-safe; outcomes complete asynchronously, never inline.
    /// </summary>
    internal sealed class PromptTracker
    {
        private readonly Action<SessionPhase> _onPhase;
        private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private bool _runActive;
        private bool _settlePending;
        private bool _aborting;

        public PromptTracker(Action<SessionPhase> onPhase)
        {
            _onPhase = onPhase;
        }

        public SessionPhase Phase { get; private set; } = SessionPhase.Idle;

        public Task<PromptOutcome> SubmitAsync(string id)
        {
            var ticket = new Ticket();
            _tickets[id] = ticket;
            Update();
            return ticket.Completion.Task;
        }

        /// <summary>Successful <c>prompt</c> response.</summary>
        public void Acknowledged(string id, bool? agentInvoked)
        {
            if (!_tickets.TryGetValue(id, out var ticket)) return;
            if (agentInvoked == false) Complete(id, new PromptOutcome { Status = PromptStatus.Local, SessionSettled = true, Admitted = true });
            else ticket.Admitted = true;
            Update();
        }

        /// <summary>Failed <c>prompt</c> response: OMP refused the prompt, or it could not be sent.</summary>
        public void Rejected(string id, string error)
        {
            Complete(id, new PromptOutcome { Status = PromptStatus.Error, Error = error, SessionSettled = true });
            Update();
        }

        public void AgentStart()
        {
            _runActive = true;
            _settlePending = true;
            Update();
        }

        public void AgentEnd(bool yielded)
        {
            if (yielded)
            {
                _runActive = false;
                if (_tickets.Count == 0) _aborting = false;
            }
            Update();
        }

        public void PromptResult(JObject frame)
        {
            var settled = Json.Bool(frame, "sessionSettled") == true;
            var id = Json.Str(frame, "id");
            if (id != null)
            {
                Complete(id, new PromptOutcome
                {
                    Status = ParseStatus(Json.Str(frame, "status")),
                    Error = Json.Str(Json.Get(frame, "error"), "message"),
                    SessionSettled = settled,
                    Admitted = true,
                });
            }
            if (settled)
            {
                _settlePending = false;
                _runActive = false;
            }
            else if (Json.Bool(frame, "agentInvoked") == true)
            {
                _settlePending = true;
            }
            if (_tickets.Count == 0) _aborting = false;
            Update();
        }

        public void SessionSettled()
        {
            _settlePending = false;
            _runActive = false;
            if (_tickets.Count == 0) _aborting = false;
            Update();
        }

        /// <summary>Marks the current run as aborting; ignored when nothing runs (idle or yielded), so it never carries into the next prompt.</summary>
        public void AbortRequested()
        {
            if (_tickets.Count == 0 && !_runActive) return;
            _aborting = true;
            Update();
        }

        /// <summary>The process went away: every open prompt resolves with an error, admitted when OMP had acknowledged it.</summary>
        public void Terminate(string error)
        {
            foreach (var pair in _tickets.ToList())
                Complete(pair.Key, new PromptOutcome { Status = PromptStatus.Error, Error = error, SessionSettled = true, Admitted = pair.Value.Admitted });
            _runActive = false;
            _settlePending = false;
            _aborting = false;
            Update();
        }

        private static PromptStatus ParseStatus(string? status)
        {
            switch (status)
            {
                case "completed": return PromptStatus.Completed;
                case "aborted": return PromptStatus.Aborted;
                default: return PromptStatus.Error;
            }
        }

        private void Complete(string id, PromptOutcome outcome)
        {
            if (!_tickets.TryGetValue(id, out var ticket)) return;
            _tickets.Remove(id);
            ticket.Completion.TrySetResult(outcome);
        }

        private void Update()
        {
            var phase = Derive();
            if (phase == Phase) return;
            Phase = phase;
            _onPhase(phase);
        }

        private SessionPhase Derive()
        {
            var busy = _tickets.Count > 0 || _runActive;
            if (_aborting && busy) return SessionPhase.Aborting;
            if (_runActive) return SessionPhase.Running;
            if (_tickets.Count > 0) return _tickets.Values.Any(t => t.Admitted) ? SessionPhase.Running : SessionPhase.Submitting;
            return _settlePending ? SessionPhase.Yielded : SessionPhase.Idle;
        }

        private sealed class Ticket
        {
            public bool Admitted { get; set; }
            public TaskCompletionSource<PromptOutcome> Completion { get; } = new TaskCompletionSource<PromptOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
