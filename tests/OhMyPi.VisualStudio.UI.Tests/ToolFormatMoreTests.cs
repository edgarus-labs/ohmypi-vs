using Newtonsoft.Json.Linq;
using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class ToolFormatMoreTests
{
    [Fact]
    public void The_changed_part_of_a_pair_is_widened_to_whole_words_and_a_pair_with_nothing_in_common_is_unmarked()
    {
        var spans = ToolFormat.ChangedSpans(new[] { "-var count = 1;", "+var count = 2;", "-completely", "+different", " context", "-same", "+same" });
        Assert.Equal((12, 1), spans[0]);
        Assert.Equal((12, 1), spans[1]);
        Assert.Null(spans[2]);
        Assert.Null(spans[3]);
        Assert.Null(spans[5]);
    }

    [Fact]
    public void A_change_inside_a_word_marks_the_whole_word()
    {
        var spans = ToolFormat.ChangedSpans(new[] { "-return counter;", "+return counters;" });
        Assert.Equal((7, 7), spans[0]);
        Assert.Equal((7, 8), spans[1]);
    }

    [Fact]
    public void Removed_lines_without_added_partners_stay_unmarked()
    {
        Assert.All(ToolFormat.ChangedSpans(new[] { "-only removed", "-and another" }), span => Assert.Null(span));
        Assert.Empty(ToolFormat.ChangedSpans(new string[0]));
    }

    [Fact]
    public void The_diffs_of_a_multi_file_edit_are_labelled_with_their_files_and_a_missing_diff_gives_none()
    {
        var details = JToken.Parse("{\"perFileResults\":[{\"path\":\"a.cs\",\"diff\":\"@@\\n-a\\n+b\"},{\"path\":\"b.cs\"},{\"diff\":\"@@\\n-c\\n+d\"}]}");
        Assert.Equal("--- a.cs\n@@\n-a\n+b\n--- \n@@\n-c\n+d", ToolFormat.CollectDiff(details));
        Assert.Equal("@@ single", ToolFormat.CollectDiff(JToken.Parse("{\"perFileResults\":[{\"path\":\"b.cs\"}],\"diff\":\"@@ single\"}")));
        Assert.Null(ToolFormat.CollectDiff(JToken.Parse("{\"perFileResults\":[]}")));
        Assert.Null(ToolFormat.CollectDiff(null));
    }

    [Fact]
    public void The_path_of_a_call_may_be_a_list_and_a_list_without_text_gives_no_path()
    {
        ToolItem Read(string args) => new ToolItem { Id = "t", Name = "read", Args = JToken.Parse(args), Status = ToolStatus.Done, StartedAt = 1000, EndedAt = 1010, Result = new ToolResultView { Text = "x" } };
        Assert.Equal("a.cs, b.cs", ToolFormat.Headline(Read("{\"paths\":[\"a.cs\",3,\"b.cs\"]}"), null).Primary);
        Assert.Equal("", ToolFormat.Headline(Read("{\"paths\":[3]}"), null).Primary);
    }

    [Fact]
    public void Hits_fall_back_to_counting_lines_when_the_details_hold_no_count()
    {
        var result = new ToolResultView { Text = "a\n\n b \n", Details = JToken.Parse("{\"other\":1}") };
        Assert.Equal(2, ToolFormat.HitCount(result));
    }

    [Fact]
    public void Agents_that_name_each_other_as_parents_are_all_still_listed()
    {
        var agents = new[]
        {
            new AgentView { Id = "a", ParentId = "b", Name = "A", Status = AgentStatus.Running },
            new AgentView { Id = "b", ParentId = "a", Name = "B", Status = AgentStatus.Pending },
        };
        Assert.Equal(new[] { "a", "b" }, AgentRows.Tree(agents).Select(row => row.Agent.Id).OrderBy(id => id));
    }
}
