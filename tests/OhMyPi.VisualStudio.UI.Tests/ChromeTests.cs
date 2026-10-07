using System;
using Omp.Core;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests
{
    public class ChromeTests
    {
        [Fact]
        public void StateWord_derives_header_state()
        {
            Assert.Equal(StateWord.Idle, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Idle, 0, false));
            Assert.Equal(StateWord.Working, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Submitting, 0, false));
            Assert.Equal(StateWord.Working, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Running, 0, false));
            Assert.Equal(StateWord.Working, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Yielded, 0, false));
            Assert.Equal(StateWord.Aborting, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Aborting, 0, false));
            Assert.Equal(StateWord.Waiting, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Running, 1, false));
            Assert.Equal(StateWord.Starting, Chrome.GetStateWord(ConnectionState.Starting, SessionPhase.Idle, 0, false));
            Assert.Equal(StateWord.Starting, Chrome.GetStateWord(ConnectionState.Restarting, SessionPhase.Running, 0, false));
            Assert.Equal(StateWord.Offline, Chrome.GetStateWord(ConnectionState.Stopped, SessionPhase.Idle, 0, false));
            Assert.Equal(StateWord.Offline, Chrome.GetStateWord(ConnectionState.Failed, SessionPhase.Idle, 0, false));
            Assert.Equal(StateWord.Offline, Chrome.GetStateWord(ConnectionState.Ready, SessionPhase.Idle, 0, true));
        }

        [Fact]
        public void TodoSummary_names_task_in_progress()
        {
            Assert.Null(Chrome.TodoSummary(Array.Empty<TodoPhaseView>()));
            Assert.Null(Chrome.TodoSummary(new[] { new TodoPhaseView { Name = "p" } }));
            var phases = new[]
            {
                new TodoPhaseView { Name = "Plan", Tasks = new[] { new TodoTaskView { Content = "Read code", Status = "completed" }, new TodoTaskView { Content = "Sketch", Status = "completed" } } },
                new TodoPhaseView
                {
                    Name = "Build",
                    Tasks = new[]
                    {
                        new TodoTaskView { Content = "Write   the\nparser", Status = "in_progress" },
                        new TodoTaskView { Content = "Test", Status = "pending" },
                        new TodoTaskView { Content = "Ship", Status = "pending" },
                    },
                },
            };
            Assert.Equal("todos 2/5 · current: Write the parser", Chrome.TodoSummary(phases));
            Assert.Equal("todos 0/1", Chrome.TodoSummary(new[] { new TodoPhaseView { Name = "p", Tasks = new[] { new TodoTaskView { Content = "a" } } } }));
        }

        [Fact]
        public void UsageText_formats_context_and_cost()
        {
            Assert.Equal("", Chrome.UsageText(null, null));
            Assert.Equal("ctx 13%", Chrome.UsageText(new ContextUsageView { Tokens = 13_000, ContextWindow = 100_000, Percent = 13.2 }, null));
            Assert.Equal("$0.0215", Chrome.UsageText(null, 0.02149));
            Assert.Equal("ctx 50% · $3.46", Chrome.UsageText(new ContextUsageView { Tokens = 1, ContextWindow = 2, Percent = 50 }, 3.456));
        }

        [Fact]
        public void ElapsedText_is_the_shared_duration_format_and_empty_below_a_second()
        {
            Assert.Equal("", Chrome.ElapsedText(0));
            Assert.Equal("", Chrome.ElapsedText(999));
            Assert.Equal("", Chrome.ElapsedText(-5));
            Assert.Equal(Format.FormatDuration(12_400), Chrome.ElapsedText(12_400));
            Assert.Equal(Format.FormatDuration(65_000), Chrome.ElapsedText(65_000));
            Assert.Equal(Format.FormatDuration(3_725_000), Chrome.ElapsedText(3_725_000));
        }

        [Fact]
        public void LineCount_pluralizes()
        {
            Assert.Equal("1 line", Chrome.LineCount("a"));
            Assert.Equal("3 lines", Chrome.LineCount("a\nb\nc"));
        }

        [Fact]
        public void ComposerButton_sends_when_idle_and_stops_while_busy()
        {
            var idle = Chrome.ComposerButtons(busy: false, hasContent: true, unavailable: false);
            Assert.False(idle.IsStop);
            Assert.True(idle.Enabled);

            Assert.False(Chrome.ComposerButtons(false, false, false).Enabled);
            Assert.False(Chrome.ComposerButtons(false, true, true).Enabled);

            var busy = Chrome.ComposerButtons(busy: true, hasContent: false, unavailable: false);
            Assert.True(busy.IsStop);
            Assert.True(busy.Enabled);
        }

        [Fact]
        public void Enter_steers_a_running_turn_and_alt_enter_queues_a_follow_up()
        {
            Assert.Equal(PromptMode.Auto, Chrome.SendMode(busy: false, followUp: false));
            Assert.Equal(PromptMode.Auto, Chrome.SendMode(busy: false, followUp: true));
            Assert.Equal(PromptMode.Auto, Chrome.SendMode(busy: true, followUp: false));
            Assert.Equal(PromptMode.FollowUp, Chrome.SendMode(busy: true, followUp: true));
        }
    }
}
