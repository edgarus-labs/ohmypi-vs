using Omp.Core;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The agents detail, the welcome screen's recent sessions, queued messages and the todo list.</summary>
[Collection("wpf")]
public sealed class PanelsTests
{
    private static AgentView Agent(string id, AgentStatus status, string? activity = null) =>
        new AgentView { Id = id, Name = "worker-" + id, Status = status, Activity = activity, StartedAt = 1 };

    private static FakeService WithAgents(params AgentView[] agents) => new FakeService { Agents = agents };

    private static Button AgentRow(Window window, string id) =>
        Descendants(window).OfType<Button>().First(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b).StartsWith(id + " ", StringComparison.Ordinal));

    private static Button Visible(Window window, string text) =>
        Descendants(window).OfType<Button>().First(b => b.IsVisible && b.Content is string s && s == text);

    private static void OpenDetail(Window window, OmpChatControl control, string id)
    {
        control.ShowAgents();
        Pump();
        Click(AgentRow(window, id));
        Pump();
    }

    [Fact]
    public void An_agent_row_opens_and_closes_its_detail() => RunSta((window, control) =>
                                                                   {
                                                                       OpenDetail(window, control, "a1");
                                                                       Assert.True(HasText(window, "Transcript"));
                                                                       Click(AgentRow(window, "a1"));
                                                                       Pump();
                                                                       Assert.DoesNotContain(Descendants(window).OfType<Button>(), b => b.IsVisible && b.Content is string s && s == "Transcript");
                                                                   }, WithAgents(Agent("a1", AgentStatus.Running), Agent("a2", AgentStatus.Pending), Agent("a3", AgentStatus.Completed), Agent("a4", AgentStatus.Failed), Agent("a5", AgentStatus.Aborted)), new FakeHost());

    [Fact]
    public void Steering_sends_the_trimmed_message_and_escape_or_empty_text_sends_nothing()
    {
        var service = WithAgents(Agent("a1", AgentStatus.Running));
        RunSta((window, control) =>
        {
            OpenDetail(window, control, "a1");
            Click(Visible(window, "Steer"));
            Pump();
            var input = Named<TextBox>(window, "Message for the agent");
            Press(input, Key.Enter);
            input.Text = "  focus on tests  ";
            Press(input, Key.Enter);
            Pump();
            Assert.Equal(new[] { ("a1", "focus on tests") }, service.Steers);
            Assert.False(input.IsVisible);

            Click(Visible(window, "Steer"));
            Pump();
            input.Text = "never sent";
            Press(input, Key.Escape);
            Pump();
            Assert.False(input.IsVisible);
            Press(input, Key.A);
            Assert.Single(service.Steers);
        }, service, new FakeHost());
    }

    [Fact]
    public void Cancelling_asks_first_and_reports_an_agent_that_was_no_longer_running()
    {
        var service = WithAgents(Agent("a1", AgentStatus.Running, "running tests"));
        service.CancelResult = false;
        RunSta((window, control) =>
        {
            OpenDetail(window, control, "a1");
            Click(Visible(window, "Cancel"));
            Pump();
            Assert.True(HasText(window, "Cancel agent worker-a1 (a1)?\nrunning tests"));
            Click(Visible(window, "Keep Running"));
            Pump();
            Assert.Empty(service.Cancels);

            Click(Visible(window, "Cancel"));
            Pump();
            Click(Visible(window, "Cancel Agent"));
            Pump();
            Assert.Equal(new[] { "a1" }, service.Cancels);
            Assert.True(HasText(window, "Agent worker-a1 was not running."));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_finished_agent_offers_no_cancel_and_its_open_confirmation_closes()
    {
        var service = WithAgents(Agent("a1", AgentStatus.Running));
        RunSta((window, control) =>
        {
            OpenDetail(window, control, "a1");
            Click(Visible(window, "Cancel"));
            Pump();
            service.RaiseAgents(new[] { Agent("a1", AgentStatus.Completed) });
            Pump(300);
            Assert.DoesNotContain(Descendants(window).OfType<Button>(), b => b.IsVisible && b.Content is string s && (s == "Cancel" || s == "Cancel Agent"));
        }, service, new FakeHost());
    }

    [Fact]
    public void The_transcript_of_an_agent_loads_copies_as_markdown_and_says_when_it_is_empty()
    {
        var service = WithAgents(Agent("a1", AgentStatus.Completed));
        service.AgentTranscript = new TranscriptItem[] { new UserItem { Id = "u", Text = "do it" }, new AssistantItem { Id = "a", Text = "done" } };
        RunSta((window, control) =>
        {
            OpenDetail(window, control, "a1");
            Click(Visible(window, "Transcript"));
            Pump(300);
            Assert.True(HasText(window, "do it"));
            Click(Named<Button>(window, "Copy as Markdown"));
            Pump();
            Assert.Contains("do it", Clipboard.GetText());

            service.AgentTranscript = new TranscriptItem[0];
            Click(Visible(window, "Transcript"));
            Pump(300);
            Assert.True(HasText(window, "The transcript is empty."));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_transcript_that_cannot_load_is_reported_and_leaves_nothing_behind()
    {
        var service = WithAgents(Agent("a1", AgentStatus.Completed));
        service.AgentTranscriptError = new InvalidOperationException("gone");
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            OpenDetail(window, control, "a1");
            Click(Visible(window, "Transcript"));
            Pump(300);
            Assert.False(HasText(window, "Loading transcript…"));
            Assert.Contains(host.Errors, error => error.Error.Message == "gone");
        }, service, host);
    }

    [Fact]
    public void The_welcome_screen_resumes_recent_sessions_and_offers_all_when_there_are_more()
    {
        var sessions = Enumerable.Range(1, 9).Select(i => new SessionSummary { Path = $"C:\\s\\{i}.jsonl", Title = $"Session {i}", Modified = 1_000_000L * i, Size = 10 }).ToArray();
        var service = new FakeService { Sessions = sessions, Session = new SessionView { SessionFile = "C:\\s\\current.jsonl" } };
        RunSta((window, control) =>
        {
            Pump(300);
            var resume = Descendants(window).OfType<Button>().Where(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Resume ", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(resume);
            Assert.True(resume.Count < sessions.Length);
            Assert.True(HasText(window, $"View all ({sessions.Length})"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Queued_messages_show_as_chips_until_they_are_sent()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            service.RaiseSession(new SessionView { Queue = new QueueView { Steering = new[] { "use the cache" }, FollowUp = new[] { "then\nrun tests" } } });
            Pump(300);
            Assert.NotNull(Named<FrameworkElement>(window, "Queued steer: use the cache"));
            Assert.NotNull(Named<FrameworkElement>(window, "Queued follow-up: then\nrun tests"));

            service.RaiseSession(new SessionView());
            Pump(300);
            Assert.Empty(AllNamed<FrameworkElement>(window, "Queued steer: use the cache"));
        }, service, new FakeHost());
    }

    private static SessionView Todos(string status) => new SessionView
    {
        Todos = new[]
        {
            new TodoPhaseView { Name = "Build", Tasks = new[] { new TodoTaskView { Content = "write code", Status = status }, new TodoTaskView { Content = "odd", Status = "someday" } } },
            new TodoPhaseView { Name = "Verify", Tasks = new[] { new TodoTaskView { Content = "run tests", Status = "pending" } } },
        },
    };

    [Fact]
    public void A_closed_todo_list_stays_closed_across_restarts_until_its_tasks_change()
    {
        var host = new FakeDismissingHost();
        var first = new FakeService();
        RunSta((window, control) =>
        {
            first.RaiseSession(Todos("completed"));
            Pump(300);
            Assert.True(HasText(window, "todos 1/3"));
            Click(Named<Button>(window, "Close the todo list"));
            Pump();
            Assert.False(HasText(window, "todos 1/3"));
            Assert.Single(host.Dismissed);
        }, first, host);

        var second = new FakeService();
        RunSta((window, control) =>
        {
            second.RaiseSession(Todos("completed"));
            Pump(300);
            Assert.False(HasText(window, "todos 1/3"));

            second.RaiseSession(Todos("in_progress"));
            Pump(300);
            Assert.True(HasText(window, "todos 0/3"));
        }, second, host);
    }

    [Fact]
    public void Closing_the_todo_list_without_a_place_to_remember_it_hides_it_for_now()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            service.RaiseSession(Todos("completed"));
            Pump(300);
            Click(Named<Button>(window, "Close the todo list"));
            Pump();
            Assert.False(HasText(window, "todos 1/3"));
            service.RaiseSession(new SessionView { LastError = "boom", Todos = Todos("completed").Todos });
            Pump(300);
            Assert.False(HasText(window, "todos 1/3"));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_todo_list_that_cannot_be_remembered_is_still_closed_and_the_failure_logged()
    {
        var host = new FakeDismissingHost { DismissError = new InvalidOperationException("settings store is read-only") };
        var service = new FakeService();
        RunSta((window, control) =>
        {
            service.RaiseSession(Todos("completed"));
            Pump(300);
            Click(Named<Button>(window, "Close the todo list"));
            Pump();
            Assert.False(HasText(window, "todos 1/3"));
            Assert.Contains(host.Errors, error => error.Message == "Remembering the closed todo list failed");
        }, service, host);
    }
}
