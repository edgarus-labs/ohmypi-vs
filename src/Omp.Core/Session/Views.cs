using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Session
{
    /// <summary>Copies of the published view models, so every change publishes a fresh instance.</summary>
    internal static class Views
    {
        public static SessionView Copy(SessionView v) => new SessionView
        {
            Phase = v.Phase,
            SessionId = v.SessionId,
            SessionFile = v.SessionFile,
            SessionName = v.SessionName,
            Model = v.Model,
            ThinkingLevel = v.ThinkingLevel,
            ThinkingSelector = v.ThinkingSelector,
            ThinkingResolved = v.ThinkingResolved,
            AvailableThinkingLevels = v.AvailableThinkingLevels,
            FastModeEnabled = v.FastModeEnabled,
            FastModeActive = v.FastModeActive,
            IsCompacting = v.IsCompacting,
            Queue = v.Queue,
            ContextUsage = v.ContextUsage,
            Todos = v.Todos,
            CostUsd = v.CostUsd,
            LastError = v.LastError,
        };

        public static AgentView Copy(AgentView a) => new AgentView
        {
            Id = a.Id,
            ParentId = a.ParentId,
            Name = a.Name,
            Description = a.Description,
            Model = a.Model,
            Status = a.Status,
            Activity = a.Activity,
            ToolCount = a.ToolCount,
            Tokens = a.Tokens,
            CostUsd = a.CostUsd,
            StartedAt = a.StartedAt,
            EndedAt = a.EndedAt,
            SessionFile = a.SessionFile,
            Error = a.Error,
        };

        public static bool Same(AgentView a, AgentView b) =>
            a.Id == b.Id && a.ParentId == b.ParentId && a.Name == b.Name && a.Description == b.Description && a.Model == b.Model &&
            a.Status == b.Status && a.Activity == b.Activity && a.ToolCount == b.ToolCount && a.Tokens == b.Tokens && a.CostUsd == b.CostUsd &&
            a.StartedAt == b.StartedAt && a.EndedAt == b.EndedAt && a.SessionFile == b.SessionFile && a.Error == b.Error;

        public static AssistantItem Copy(AssistantItem i) => new AssistantItem
        {
            Id = i.Id,
            Text = i.Text,
            Thinking = i.Thinking,
            Streaming = i.Streaming,
            Model = i.Model,
            StopReason = i.StopReason,
            ErrorMessage = i.ErrorMessage,
            Usage = i.Usage,
        };

        public static ToolItem Copy(ToolItem i) => new ToolItem
        {
            Id = i.Id,
            Name = i.Name,
            Args = i.Args,
            Status = i.Status,
            Partial = i.Partial,
            Result = i.Result,
            StartedAt = i.StartedAt,
            EndedAt = i.EndedAt,
        };
    }

    internal static class ModelMapper
    {
        /// <summary>Map OMP's wire model descriptor to the host view model.</summary>
        public static ModelView ToModelView(JObject model)
        {
            var id = Json.Str(model, "id") ?? "";
            var name = Json.Str(model, "name");
            var input = Json.Strings(model["input"]);
            var view = new ModelView
            {
                Provider = Json.Str(model, "provider") ?? "",
                Id = id,
                Name = string.IsNullOrEmpty(name) ? id : name!,
                Api = string.IsNullOrEmpty(Json.Str(model, "api")) ? null : Json.Str(model, "api"),
                Reasoning = Json.Bool(model, "reasoning") == true,
                ThinkingEfforts = Json.Strings(Json.Get(model["thinking"], "efforts")),
                Input = input.Count > 0 ? input : new[] { "text" },
                ContextWindow = Json.Long(model, "contextWindow"),
                MaxTokens = Json.Long(model, "maxTokens"),
            };
            if (model["cost"] is JObject cost)
            {
                view.Cost = new ModelCostView
                {
                    Input = Json.Num(cost, "input"),
                    Output = Json.Num(cost, "output"),
                };
            }
            if (model["identity"] is JObject identity)
            {
                view.VendorClass = Json.Str(identity, "class");
                view.Family = Json.Str(identity, "family");
                view.Revision = Json.Str(identity, "revision");
            }
            return view;
        }
    }
}
