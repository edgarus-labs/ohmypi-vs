using Newtonsoft.Json.Linq;
using Omp.Core.Session;

namespace Omp.Core.Tests.Session;

public class AgentRegistryTests
{
    private static JObject Lifecycle(string id, string status) => new()
    {
        ["type"] = "subagent_lifecycle",
        ["payload"] = new JObject
        {
            ["id"] = id, ["agent"] = "task", ["agentSource"] = "bundled", ["status"] = status, ["index"] = 0,
            ["description"] = $"do {id}", ["sessionFile"] = $"/s/{id}.jsonl",
        },
    };

    private static AgentView Find(AgentRegistry registry, string id) => registry.Agents.Single(a => a.Id == id);

    [Fact]
    public void SynthesizesTheMainAgentFromSessionPhaseModelAndActivity()
    {
        var registry = new AgentRegistry();
        var changes = 0;
        registry.Changed += _ => changes++;
        registry.SetMain(SessionPhase.Idle, "anthropic/claude", null);
        var main = Assert.Single(registry.Agents);
        Assert.Equal(("main", "main", AgentStatus.Completed, "anthropic/claude"), (main.Id, main.Name, main.Status, main.Model));
        registry.SetMain(SessionPhase.Running, "anthropic/claude", "read");
        Assert.Equal(AgentStatus.Running, registry.Agents[0].Status);
        Assert.Equal("read", registry.Agents[0].Activity);
        var before = changes;
        registry.SetMain(SessionPhase.Running, "anthropic/claude", "read");
        Assert.Equal(before, changes);
    }

    [Fact]
    public void TracksSubagentsThroughLifecycleAndProgressFrames()
    {
        var registry = new AgentRegistry();
        registry.SetMain(SessionPhase.Running, null, null);
        registry.ApplyFrame(Lifecycle("Anna", "started"));
        var anna = Find(registry, "Anna");
        Assert.Equal(AgentStatus.Running, anna.Status);
        Assert.Equal("main", anna.ParentId);
        Assert.Equal("task", anna.Name);
        Assert.Equal("do Anna", anna.Description);
        Assert.Equal("/s/Anna.jsonl", anna.SessionFile);
        Assert.NotNull(anna.StartedAt);

        registry.ApplyFrame(new JObject
        {
            ["type"] = "subagent_progress",
            ["payload"] = new JObject
            {
                ["index"] = 0, ["agent"] = "task", ["agentSource"] = "bundled", ["task"] = "t",
                ["progress"] = new JObject { ["id"] = "Anna", ["status"] = "running", ["currentTool"] = "grep", ["toolCount"] = 4, ["tokens"] = 1200, ["cost"] = 0.02, ["resolvedModel"] = "openai/gpt" },
            },
        });
        anna = Find(registry, "Anna");
        Assert.Equal("grep", anna.Activity);
        Assert.Equal(4, anna.ToolCount);
        Assert.Equal(1200, anna.Tokens);
        Assert.Equal(0.02, anna.CostUsd);
        Assert.Equal("openai/gpt", anna.Model);

        registry.ApplyFrame(Lifecycle("Anna", "failed"));
        anna = Find(registry, "Anna");
        Assert.Equal(AgentStatus.Failed, anna.Status);
        Assert.NotNull(anna.EndedAt);
        Assert.Null(anna.Activity);
    }

    [Fact]
    public void DerivesNestedParentsFromDottedIds()
    {
        var registry = new AgentRegistry();
        registry.ApplyFrame(Lifecycle("Anna", "started"));
        registry.ApplyFrame(Lifecycle("Anna.Bob", "started"));
        Assert.Equal("Anna", Find(registry, "Anna.Bob").ParentId);
    }

    [Fact]
    public void IgnoresProgressForUnknownSubagentsAndAppliesSnapshots()
    {
        var registry = new AgentRegistry();
        registry.ApplyFrame(new JObject
        {
            ["type"] = "subagent_progress",
            ["payload"] = new JObject { ["index"] = 0, ["agent"] = "task", ["agentSource"] = "bundled", ["task"] = "t", ["progress"] = new JObject { ["id"] = "Ghost", ["status"] = "running" } },
        });
        Assert.Single(registry.Agents);
        registry.ApplySnapshot(registry.Version, new JArray(new JObject
        {
            ["id"] = "Cleo", ["index"] = 1, ["agent"] = "scout", ["agentSource"] = "bundled", ["status"] = "running", ["lastUpdate"] = 10_000, ["task"] = "look",
            ["progress"] = new JObject { ["lastIntent"] = "Scanning", ["durationMs"] = 4000, ["retryFailure"] = new JObject { ["attempt"] = 2, ["errorMessage"] = "429" } },
        }));
        var cleo = Find(registry, "Cleo");
        Assert.Equal("scout", cleo.Name);
        Assert.Equal("look", cleo.Description);
        Assert.Equal("Scanning", cleo.Activity);
        Assert.Equal(6000, cleo.StartedAt);
        Assert.Equal("429", cleo.Error);
    }

    [Fact]
    public void IgnoresALifecycleEndForAnUnknownSubagent()
    {
        var registry = new AgentRegistry();
        registry.ApplyFrame(Lifecycle("Ghost", "completed"));
        Assert.Single(registry.Agents);
    }

    [Fact]
    public void ASnapshotReplacesSubagentsButKeepsMain()
    {
        var registry = new AgentRegistry();
        registry.ApplyFrame(Lifecycle("Anna", "started"));
        registry.ApplySnapshot(registry.Version, new JArray(new JObject { ["id"] = "Bob", ["agent"] = "task", ["status"] = "running", ["lastUpdate"] = 10_000 }));
        Assert.Equal(new[] { "main", "Bob" }, registry.Agents.Select(a => a.Id));
    }

    [Fact]
    public void KeepsSubagentFramesNewerThanTheSnapshot()
    {
        var registry = new AgentRegistry();
        registry.ApplyFrame(Lifecycle("Anna", "started"));
        var requested = registry.Version;
        registry.ApplyFrame(Lifecycle("Anna", "completed"));
        registry.ApplyFrame(Lifecycle("Bob", "started"));
        registry.ApplySnapshot(requested, new JArray(new JObject { ["id"] = "Anna", ["agent"] = "task", ["status"] = "running", ["lastUpdate"] = 10_000 }));
        Assert.Equal(new[] { "main:Completed", "Anna:Completed", "Bob:Running" }, registry.Agents.Select(a => $"{a.Id}:{a.Status}"));
    }

    [Fact]
    public void EndsAFinishedSubagentOfASnapshotAtItsLastUpdate()
    {
        var registry = new AgentRegistry();
        registry.ApplySnapshot(registry.Version, new JArray(
            new JObject { ["id"] = "Done", ["agent"] = "task", ["status"] = "completed", ["lastUpdate"] = 10_000, ["progress"] = new JObject { ["durationMs"] = 4000 } },
            new JObject { ["id"] = "Busy", ["agent"] = "task", ["status"] = "running", ["lastUpdate"] = 10_000 }));
        var done = Find(registry, "Done");
        Assert.Equal((6000L, 10_000L), (done.StartedAt!.Value, done.EndedAt!.Value));
        Assert.Null(Find(registry, "Busy").EndedAt);
    }
}
