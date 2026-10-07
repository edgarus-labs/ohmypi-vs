using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using Omp.Core.Changes;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class RowsTests
{
    private static AgentView Agent(string id, string name = "task", AgentStatus status = AgentStatus.Completed, string? activity = null, string? parent = null) =>
        new AgentView { Id = id, Name = name, Status = status, Activity = activity, ParentId = parent };

    [Fact]
    public void Agent_label_is_the_omp_name()
    {
        Assert.Equal("main", AgentRows.Label(Agent("main", "main")));
        Assert.Equal("ReviewAuth", AgentRows.Label(Agent("ReviewAuth", "scout")));
        Assert.Equal("Tests", AgentRows.Label(Agent("Planner.Tests")));
    }

    [Fact]
    public void Agent_description_is_type_status_activity()
    {
        Assert.Equal("scout · running · read src/a.ts", AgentRows.Description(Agent("ReviewAuth", "scout", AgentStatus.Running, "read src/a.ts")));
        Assert.Equal("completed", AgentRows.Description(Agent("main", "main")));
        var longText = AgentRows.Description(Agent("a", status: AgentStatus.Running, activity: new string('x', 100)));
        Assert.True(longText.Length <= 50, longText);
        Assert.EndsWith("…", longText);
        Assert.Equal("task · running · line one", AgentRows.Description(Agent("a", status: AgentStatus.Running, activity: "line one\nline two")));
    }

    [Fact]
    public void Agents_sort_running_first_then_pending_stable()
    {
        var sorted = AgentRows.Sort(new[]
        {
            Agent("1"), Agent("2", status: AgentStatus.Running), Agent("3", status: AgentStatus.Pending), Agent("4", status: AgentStatus.Failed), Agent("5", status: AgentStatus.Running),
        });
        Assert.Equal(new[] { "2", "5", "3", "1", "4" }, sorted.Select(a => a.Id));
    }

    [Fact]
    public void Agents_title_counts_agents_and_running()
    {
        Assert.Equal("Agents", AgentRows.Title(new AgentView[0]));
        Assert.Equal("Agents 1", AgentRows.Title(new[] { Agent("main", "main") }));
        Assert.Equal("Agents 3 · 2 running", AgentRows.Title(new[] { Agent("a", status: AgentStatus.Running), Agent("b", status: AgentStatus.Running), Agent("c") }));
    }

    [Fact]
    public void Agent_tree_nests_subagents_under_parent_or_main()
    {
        var rows = AgentRows.Tree(new[]
        {
            Agent("main", "main", AgentStatus.Running),
            Agent("Done", status: AgentStatus.Completed),
            Agent("Planner", status: AgentStatus.Running),
            Agent("Planner.Tests", status: AgentStatus.Running, parent: "Planner"),
            Agent("Orphan", status: AgentStatus.Pending, parent: "missing"),
        });
        Assert.Equal(new[] { "main:0", "Planner:1", "Planner.Tests:2", "Orphan:1", "Done:1" }, rows.Select(r => $"{r.Agent.Id}:{r.Depth}"));
    }

    [Fact]
    public void Agent_tree_without_main_keeps_roots()
    {
        var rows = AgentRows.Tree(new[] { Agent("A"), Agent("B", status: AgentStatus.Running) });
        Assert.Equal(new[] { "B:0", "A:0" }, rows.Select(r => $"{r.Agent.Id}:{r.Depth}"));
    }

    [Fact]
    public void Change_rows_show_status_folder_and_counts()
    {
        Assert.Equal("", ChangeRows.Summary(0));
        Assert.Equal("1 file", ChangeRows.Summary(1));
        Assert.Equal("3 files", ChangeRows.Summary(3));
        Assert.Equal("Changes", ChangeRows.Title(0));
        Assert.Equal("Changes · 3 files", ChangeRows.Title(3));
        Assert.Equal("M", ChangeRows.StatusLetter(ChangeStatus.Modified));
        Assert.Equal("A", ChangeRows.StatusLetter(ChangeStatus.Added));
        Assert.Equal("D", ChangeRows.StatusLetter(ChangeStatus.Deleted));
        Assert.Equal("src\\auth · +3 −1", ChangeRows.Detail(new TrackedChange { Path = "C:\\w\\src\\auth\\A.cs", Status = ChangeStatus.Modified, Added = 3, Removed = 1 }, "C:\\w"));
        Assert.Equal("+4 −0", ChangeRows.Detail(new TrackedChange { Path = "C:\\w\\A.cs", Status = ChangeStatus.Added, Added = 4 }, "C:\\w\\"));
        Assert.Equal("", ChangeRows.Detail(new TrackedChange { Path = "C:\\w\\old.cs", Status = ChangeStatus.Deleted, Removed = 9 }, "C:\\w"));
        Assert.Equal("C:\\x\\y · +1 −1", ChangeRows.Detail(new TrackedChange { Path = "C:\\x\\y\\B.cs", Status = ChangeStatus.Modified, Added = 1, Removed = 1 }, null));
        Assert.Equal("B.cs", ChangeRows.FileName(new TrackedChange { Path = "C:\\x\\y\\B.cs" }));
    }
}
