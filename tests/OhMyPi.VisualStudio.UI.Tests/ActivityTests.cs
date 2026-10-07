using System.Linq;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;
using Xunit;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class ActivityTests
    {
        [Theory]
        [InlineData(SessionPhase.Idle, false, null)]
        [InlineData(SessionPhase.Submitting, false, "Sending")]
        [InlineData(SessionPhase.Running, false, "Working")]
        [InlineData(SessionPhase.Running, true, "Compacting")]
        [InlineData(SessionPhase.Yielded, false, "Finishing")]
        [InlineData(SessionPhase.Aborting, false, "Stopping")]
        public void Label_follows_the_prompt_lifecycle(SessionPhase phase, bool compacting, string? expected)
        {
            Assert.Equal(expected, ActivityText.Label(new SessionView { Phase = phase, IsCompacting = compacting }));
        }

        [Fact]
        public void The_activity_row_stays_below_everything_that_arrives_while_it_shows()
        {
            var list = new TranscriptList();
            list.Upsert(new UserItem { Id = "u1", Text = "hi" });
            list.SetActivity(new ActivityItem("Working", 0));
            list.Upsert(new AssistantItem { Id = "a1", Text = "He" });
            list.Upsert(new AssistantItem { Id = "a1", Text = "Hello" });
            list.AddNotice(NoticeLevel.Info, "note");
            Assert.Equal(new[] { "u1", "a1", "local-1", ActivityItem.RowId }, list.Items.Select(i => i.Id));
            Assert.Equal("Hello", ((AssistantItem)list.Items[1]).Text);

            list.SetActivity(new ActivityItem("Finishing", 0));
            Assert.Equal("Finishing", ((ActivityItem)list.Items.Last()).Label);
            Assert.Equal(4, list.Items.Count);

            list.Reset(new TranscriptItem[] { new UserItem { Id = "u2", Text = "again" } });
            Assert.Equal(new[] { "u2", ActivityItem.RowId }, list.Items.Select(i => i.Id));

            list.SetActivity(null);
            Assert.Equal(new[] { "u2" }, list.Items.Select(i => i.Id));
            list.Upsert(new AssistantItem { Id = "a2", Text = "x" });
            Assert.Equal(new[] { "u2", "a2" }, list.Items.Select(i => i.Id));
        }

        [Fact]
        public void The_activity_row_does_not_count_as_conversation()
        {
            var list = new TranscriptList();
            list.SetActivity(new ActivityItem("Sending", 0));
            Assert.False(list.HasConversation);
        }
    }
}
