using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

namespace Omp.Core.Session;

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
