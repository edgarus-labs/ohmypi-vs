using System.Collections.Specialized;
using System.Linq;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class TranscriptStateTests
    {
        private static AssistantItem Assistant(string id, string text) => new AssistantItem { Id = id, Text = text };

        [Fact]
        public void Upsert_appends_new_ids_and_replaces_known_ids_in_place()
        {
            var list = new TranscriptList();
            var actions = new System.Collections.Generic.List<NotifyCollectionChangedAction>();
            list.Items.CollectionChanged += (_, e) => actions.Add(e.Action);

            list.Upsert(new UserItem { Id = "u1", Text = "hi" });
            list.Upsert(Assistant("a1", "He"));
            var replacement = Assistant("a1", "Hello");
            list.Upsert(replacement);

            Assert.Equal(new[] { "u1", "a1" }, list.Items.Select(i => i.Id));
            Assert.Same(replacement, list.Items[1]);
            Assert.Equal(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Replace }, actions);
        }

        [Fact]
        public void Reset_replaces_everything()
        {
            var list = new TranscriptList();
            list.Upsert(Assistant("old", "x"));
            list.Reset(new TranscriptItem[] { new UserItem { Id = "u" }, Assistant("a", "y") });
            Assert.Equal(new[] { "u", "a" }, list.Items.Select(i => i.Id));
            list.Upsert(Assistant("old", "again"));
            Assert.Equal(new[] { "u", "a", "old" }, list.Items.Select(i => i.Id));
        }

        [Fact]
        public void Local_notices_get_unique_ids_and_do_not_count_as_conversation()
        {
            var list = new TranscriptList();
            list.AddNotice(NoticeLevel.Error, "boom");
            list.AddNotice(NoticeLevel.Info, "fyi");
            Assert.Equal(new[] { "local-1", "local-2" }, list.Items.Select(i => i.Id));
            Assert.False(list.HasConversation);
            list.Upsert(new UserItem { Id = "u" });
            Assert.True(list.HasConversation);
        }

        [Fact]
        public void FollowBottom_sticks_while_at_bottom_and_follows_growth()
        {
            var follow = new FollowBottom();
            Assert.True(follow.Stick);
            Assert.True(follow.OnScrollChanged(extentHeight: 1200, offset: 600, viewportHeight: 400, extentChange: 200, viewportChange: 0, offsetChange: 0));
            Assert.False(follow.JumpVisible);
        }

        /// <summary>Feeds one scroll change the reader made with the wheel, the keys or the scroll bar.</summary>
        private static bool ReaderScroll(FollowBottom follow, double extentHeight, double offset, double viewportHeight, double extentChange, double viewportChange, double offsetChange)
        {
            follow.ReaderScrolling = true;
            var result = follow.OnScrollChanged(extentHeight, offset, viewportHeight, extentChange, viewportChange, offsetChange);
            follow.ReaderScrolling = false;
            return result;
        }

        [Fact]
        public void FollowBottom_stops_following_when_user_scrolls_up_and_offers_jump()
        {
            var follow = new FollowBottom();
            Assert.False(ReaderScroll(follow, 1000, 300, 400, 0, 0, -300));
            Assert.False(follow.Stick);
            Assert.True(follow.JumpVisible);
            Assert.False(follow.OnScrollChanged(1500, 300, 400, 500, 0, 0));
            Assert.True(follow.JumpVisible);
        }

        [Fact]
        public void FollowBottom_user_scroll_wins_over_simultaneous_extent_change()
        {
            var follow = new FollowBottom();
            Assert.False(ReaderScroll(follow, 1100, 300, 400, 100, 0, -300));
            Assert.False(follow.Stick);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(-150)]
        public void FollowBottom_keeps_following_when_layout_moves_the_view_while_content_or_zoom_changes(double offsetChange)
        {
            var follow = new FollowBottom();
            Assert.True(follow.OnScrollChanged(extentHeight: 1600, offset: 1000, viewportHeight: 300, extentChange: 200, viewportChange: -100, offsetChange: offsetChange));
            Assert.True(follow.Stick);
            Assert.False(follow.JumpVisible);
        }

        [Fact]
        public void FollowBottom_layout_moves_never_resume_following_the_reader_turned_off()
        {
            var follow = new FollowBottom();
            ReaderScroll(follow, 1000, 300, 400, 0, 0, -300);
            Assert.False(follow.OnScrollChanged(1000, 600, 400, 0, 0, 300));
            Assert.False(follow.Stick);
            Assert.True(follow.JumpVisible);
        }

        [Fact]
        public void FollowBottom_resumes_when_user_returns_to_bottom_or_jumps()
        {
            var follow = new FollowBottom();
            ReaderScroll(follow, 1000, 300, 400, 0, 0, -300);
            ReaderScroll(follow, 1000, 590, 400, 0, 0, 290);
            Assert.True(follow.Stick);
            Assert.False(follow.JumpVisible);

            ReaderScroll(follow, 1000, 0, 400, 0, 0, -590);
            follow.ToBottom();
            Assert.True(follow.Stick);
            Assert.False(follow.JumpVisible);
        }

        [Fact]
        public void FollowBottom_follows_viewport_resizes_only_while_sticking()
        {
            var follow = new FollowBottom();
            Assert.True(follow.OnScrollChanged(1000, 600, 300, 0, -100, 0));
            ReaderScroll(follow, 1000, 100, 300, 0, 0, -500);
            Assert.False(follow.OnScrollChanged(1000, 100, 200, 0, -100, 0));
        }

        [Fact]
        public void UpdateQueue_coalesces_items_by_id_in_first_arrival_order()
        {
            var queue = new UpdateQueue();
            Assert.True(queue.EnqueueItem(Assistant("a", "1")));
            Assert.False(queue.EnqueueItem(new UserItem { Id = "u" }));
            var latest = Assistant("a", "12");
            Assert.False(queue.EnqueueItem(latest));
            var session = new SessionView();
            Assert.False(queue.EnqueueSession(session));

            var batch = queue.Drain();
            Assert.Equal(new[] { "a", "u" }, batch.Items.Select(i => i.Id));
            Assert.Same(latest, batch.Items[0]);
            Assert.Same(session, batch.Session);

            Assert.True(queue.EnqueueSession(new SessionView()));
            Assert.Empty(queue.Drain().Items);
        }

        [Fact]
        public void UpdateQueue_reset_drops_pending_items_and_keeps_the_reset()
        {
            var queue = new UpdateQueue();
            queue.EnqueueItem(Assistant("a", "1"));
            var items = new TranscriptItem[] { new UserItem { Id = "u" } };
            queue.EnqueueReset(items);
            queue.EnqueueItem(Assistant("b", "2"));
            var batch = queue.Drain();
            Assert.Same(items, batch.Reset);
            Assert.Equal(new[] { "b" }, batch.Items.Select(i => i.Id));
        }

        [Fact]
        public void UpdateQueue_keeps_latest_agents_connection_and_change_flag()
        {
            var queue = new UpdateQueue();
            queue.EnqueueAgents(new AgentView[0]);
            var agents = new[] { new AgentView { Id = "main" } };
            queue.EnqueueAgents(agents);
            var connection = new ConnectionStatus { State = ConnectionState.Ready };
            queue.EnqueueConnection(connection);
            queue.EnqueueChanges();
            var batch = queue.Drain();
            Assert.Same(agents, batch.Agents);
            Assert.Same(connection, batch.Connection);
            Assert.True(batch.ChangesChanged);
            var empty = queue.Drain();
            Assert.Null(empty.Agents);
            Assert.Null(empty.Connection);
            Assert.False(empty.ChangesChanged);
        }
    }
}
