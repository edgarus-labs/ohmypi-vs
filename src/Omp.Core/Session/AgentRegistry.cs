using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Session
{
    /// <summary>
    /// The session's own agent plus its subagents, fed by <c>get_subagents</c> snapshots and
    /// <c>subagent_lifecycle</c>/<c>subagent_progress</c> frames. Not thread-safe.
    /// </summary>
    internal sealed class AgentRegistry
    {
        private readonly Dictionary<string, AgentView> _subagents = new Dictionary<string, AgentView>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        /// <summary><see cref="Version"/> at the frame that last changed each subagent.</summary>
        private readonly Dictionary<string, long> _changedAt = new Dictionary<string, long>(StringComparer.Ordinal);
        private AgentView _main = new AgentView { Id = "main", Name = "main", Status = AgentStatus.Completed };

        public AgentRegistry()
        {
            Agents = new[] { _main };
        }

        public event Action<IReadOnlyList<AgentView>>? Changed;

        public IReadOnlyList<AgentView> Agents { get; private set; }

        /// <param name="model"><c>provider/id</c> of the session model.</param>
        /// <param name="activity">Running tool name.</param>
        public void SetMain(SessionPhase phase, string? model, string? activity)
        {
            var status = phase == SessionPhase.Idle ? AgentStatus.Completed : AgentStatus.Running;
            var main = new AgentView
            {
                Id = "main",
                Name = "main",
                Status = status,
                Model = model,
                Activity = status == AgentStatus.Running ? activity : null,
            };
            if (Views.Same(main, _main)) return;
            _main = main;
            Publish();
        }

        /// <summary>Counts applied subagent frames; pass it to <see cref="ApplySnapshot"/> as the moment the snapshot was requested.</summary>
        public long Version { get; private set; }

        /// <summary>
        /// Replace the subagents with a <c>get_subagents</c> snapshot requested when <see cref="Version"/> was
        /// <paramref name="requestedAt"/>. A subagent a frame changed after that keeps the frame's state, because the
        /// snapshot is older; every other subagent is taken from the snapshot, and one in neither is dropped. A finished
        /// subagent ends at its last update.
        /// </summary>
        public void ApplySnapshot(long requestedAt, JArray? subagents)
        {
            bool ChangedSince(string id) => _changedAt.TryGetValue(id, out var at) && at > requestedAt;
            var views = new Dictionary<string, AgentView>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var snapshot in (subagents ?? new JArray()).OfType<JObject>())
            {
                var id = Json.Str(snapshot, "id");
                if (id == null || views.ContainsKey(id)) continue;
                order.Add(id);
                if (ChangedSince(id))
                {
                    views[id] = _subagents[id];
                    continue;
                }
                var view = new AgentView { Id = id, ParentId = ParentOf(id) };
                view.Name = Json.Str(snapshot, "agent") ?? "";
                view.Status = ParseStatus(Json.Str(snapshot, "status")) ?? AgentStatus.Running;
                var description = Json.Str(snapshot, "description") ?? Json.Str(snapshot, "task");
                if (!string.IsNullOrEmpty(description)) view.Description = description;
                var sessionFile = Json.Str(snapshot, "sessionFile");
                if (!string.IsNullOrEmpty(sessionFile)) view.SessionFile = sessionFile;
                var progress = snapshot["progress"] as JObject;
                var lastUpdate = (long)(Json.Num(snapshot, "lastUpdate") ?? 0);
                view.StartedAt = lastUpdate - (long)(Json.Num(progress, "durationMs") ?? 0);
                view = WithProgress(view, progress);
                if (view.Status != AgentStatus.Running && view.Status != AgentStatus.Pending) view.EndedAt = lastUpdate;
                views[id] = view;
            }
            foreach (var id in _order.Where(id => !views.ContainsKey(id) && ChangedSince(id)))
            {
                order.Add(id);
                views[id] = _subagents[id];
            }
            _subagents.Clear();
            foreach (var pair in views) _subagents[pair.Key] = pair.Value;
            _order.Clear();
            _order.AddRange(order);
            foreach (var id in _changedAt.Keys.Where(id => !views.ContainsKey(id)).ToList()) _changedAt.Remove(id);
            Publish();
        }

        public void ApplyFrame(JObject frame)
        {
            var type = Json.Str(frame, "type");
            var payload = frame["payload"] as JObject;
            if (payload == null) return;
            if (type == "subagent_lifecycle")
            {
                var id = Json.Str(payload, "id");
                if (id == null) return;
                var status = Json.Str(payload, "status");
                _subagents.TryGetValue(id, out var existing);
                if (existing == null && status != "started") return;
                var view = existing != null ? Views.Copy(existing) : new AgentView();
                view.Id = id;
                view.ParentId = ParentOf(id);
                view.Name = Json.Str(payload, "agent") ?? view.Name;
                view.Status = status == "started" ? AgentStatus.Running : ParseStatus(status) ?? view.Status;
                var description = Json.Str(payload, "description");
                if (!string.IsNullOrEmpty(description)) view.Description = description;
                var sessionFile = Json.Str(payload, "sessionFile");
                if (!string.IsNullOrEmpty(sessionFile)) view.SessionFile = sessionFile;
                if (status == "started")
                {
                    view.StartedAt ??= Listeners.NowMs();
                    view.EndedAt = null;
                }
                else
                {
                    view.EndedAt = Listeners.NowMs();
                    view.Activity = null;
                }
                Set(id, view);
                Publish();
                return;
            }
            if (type == "subagent_progress")
            {
                var progress = payload["progress"] as JObject;
                var id = Json.Str(progress, "id");
                if (id == null || !_subagents.TryGetValue(id, out var existing)) return;
                var copy = Views.Copy(existing);
                var sessionFile = Json.Str(payload, "sessionFile");
                if (!string.IsNullOrEmpty(sessionFile)) copy.SessionFile = sessionFile;
                var view = WithProgress(copy, progress);
                if (Views.Same(view, existing)) return;
                Set(id, view);
                Publish();
            }
        }

        /// <summary>Nested subagent ids are dotted (<c>Anna.Bob</c>); top-level ones belong to the main agent.</summary>
        private static string ParentOf(string id)
        {
            var dot = id.LastIndexOf('.');
            return dot == -1 ? "main" : id.Substring(0, dot);
        }

        private static AgentStatus? ParseStatus(string? status)
        {
            switch (status)
            {
                case "pending": return AgentStatus.Pending;
                case "running": return AgentStatus.Running;
                case "completed": return AgentStatus.Completed;
                case "failed": return AgentStatus.Failed;
                case "aborted": return AgentStatus.Aborted;
                default: return null;
            }
        }

        private static string? ActivityOf(JObject progress)
        {
            var tool = Json.Str(progress, "currentTool");
            if (!string.IsNullOrEmpty(tool)) return tool;
            var intent = Json.Str(progress, "lastIntent");
            if (!string.IsNullOrEmpty(intent)) return intent;
            var recent = Json.Strings(progress["recentOutput"]);
            return recent.Count > 0 ? recent[recent.Count - 1] : null;
        }

        private static AgentView WithProgress(AgentView view, JObject? progress)
        {
            if (progress == null) return view;
            var status = ParseStatus(Json.Str(progress, "status"));
            if (status != null) view.Status = status.Value;
            var description = Json.Str(progress, "description");
            if (!string.IsNullOrEmpty(description)) view.Description = description;
            var model = Json.Str(progress, "resolvedModel");
            if (!string.IsNullOrEmpty(model)) view.Model = model;
            var toolCount = Json.Num(progress, "toolCount");
            if (toolCount != null) view.ToolCount = (int)toolCount.Value;
            var tokens = Json.Num(progress, "tokens");
            if (tokens != null) view.Tokens = (long)tokens.Value;
            var cost = Json.Num(progress, "cost");
            if (cost != null) view.CostUsd = cost;
            var error = Json.Str(Json.Get(progress, "retryFailure"), "errorMessage") ?? Json.Str(progress, "error");
            if (!string.IsNullOrEmpty(error)) view.Error = error;
            var activity = ActivityOf(progress);
            view.Activity = !string.IsNullOrEmpty(activity) && (view.Status == AgentStatus.Running || view.Status == AgentStatus.Pending) ? activity : null;
            return view;
        }

        /// <summary>Stores a subagent a frame changed.</summary>
        private void Set(string id, AgentView view)
        {
            if (!_subagents.ContainsKey(id)) _order.Add(id);
            _subagents[id] = view;
            _changedAt[id] = ++Version;
        }

        private void Publish()
        {
            var agents = new List<AgentView> { _main };
            agents.AddRange(_order.Select(id => _subagents[id]));
            Agents = agents.ToArray();
            Changed?.Invoke(Agents);
        }
    }
}
