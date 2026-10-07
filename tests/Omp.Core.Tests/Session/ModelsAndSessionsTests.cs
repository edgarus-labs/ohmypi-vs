using Newtonsoft.Json.Linq;
using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

public class ModelMapperTests
{
    private static JObject Wire() => new()
    {
        ["id"] = "claude-x",
        ["name"] = "Claude X",
        ["api"] = "anthropic-messages",
        ["provider"] = "anthropic",
        ["baseUrl"] = "https://example.invalid",
        ["reasoning"] = true,
        ["input"] = new JArray("text", "image"),
        ["cost"] = new JObject { ["input"] = 3, ["output"] = 15, ["cacheRead"] = 0.3, ["cacheWrite"] = 3.75 },
        ["contextWindow"] = 200000,
        ["maxTokens"] = 64000,
        ["thinking"] = new JObject { ["mode"] = "effort", ["efforts"] = new JArray("low", "medium", "high") },
    };

    [Fact]
    public void MapsIdentityLimitsModalitiesPricingAndThinkingEfforts()
    {
        var view = ModelMapper.ToModelView(Wire());
        Assert.Equal(("anthropic", "claude-x", "Claude X", "anthropic-messages", 200000L, 64000L, true), (view.Provider, view.Id, view.Name, view.Api, view.ContextWindow!.Value, view.MaxTokens!.Value, view.Reasoning));
        Assert.Equal(new[] { "low", "medium", "high" }, view.ThinkingEfforts);
        Assert.Equal(new[] { "text", "image" }, view.Input);
        Assert.Equal(3d, view.Cost!.Input);
        Assert.Equal(15d, view.Cost.Output);
    }

    [Fact]
    public void ToleratesNullLimitsAndMissingThinkingCostAndName()
    {
        var wire = Wire();
        wire.Remove("name");
        wire["contextWindow"] = null;
        wire["maxTokens"] = null;
        wire.Remove("thinking");
        wire.Remove("cost");
        wire.Remove("input");
        var view = ModelMapper.ToModelView(wire);
        Assert.Equal("claude-x", view.Name);
        Assert.Null(view.ContextWindow);
        Assert.Null(view.MaxTokens);
        Assert.Empty(view.ThinkingEfforts);
        Assert.Equal(new[] { "text" }, view.Input);
        Assert.Null(view.Cost);
    }

    [Fact]
    public void KeepsAMissingPriceFieldMissingInsteadOfZero()
    {
        var wire = Wire();
        wire["cost"] = new JObject { ["input"] = 3 };
        var view = ModelMapper.ToModelView(wire);
        Assert.Equal(3d, view.Cost!.Input);
        Assert.Null(view.Cost.Output);
    }
}
