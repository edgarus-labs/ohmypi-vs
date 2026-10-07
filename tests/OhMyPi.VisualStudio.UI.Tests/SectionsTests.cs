using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Omp.Core;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests
{
    /// <summary>The Agents section and the session panel keep their elements while updates stream in.</summary>
    [Collection("wpf")]
    public class SectionsTests
    {
        private static AgentView[] Agents(string activity) => new[]
        {
            new AgentView { Id = "main", Name = "main", Status = AgentStatus.Running },
            new AgentView { Id = "Scout1", Name = "scout", Status = AgentStatus.Running, Activity = activity, ParentId = "main" },
        };

        [Fact]
        public void Agent_progress_keeps_the_steer_box_focused_and_its_text()
        {
            var service = new FakeService { Agents = Agents("read a.cs") };
            RunSta((window, control) =>
            {
                window.Activate();
                control.ShowAgents();
                Pump();
                Click(Named<Button>(window, "Scout1 scout · running · read a.cs"));
                Pump();
                Click(Named<Button>(window, "Steer"));
                Pump();
                var steer = Named<TextBox>(window, "Message for the agent");
                steer.Text = "focus on";
                Assert.True(steer.IsKeyboardFocused);
                Task.Run(() => service.RaiseAgents(Agents("read b.cs"))).Wait();
                Pump(200);
                Assert.True(steer.IsKeyboardFocused);
                Assert.Same(steer, Named<TextBox>(window, "Message for the agent"));
                Assert.Equal("focus on", steer.Text);
                Assert.NotNull(Named<Button>(window, "Scout1 scout · running · read b.cs"));

                var row = Named<Button>(window, "Scout1 scout · running · read b.cs");
                Keyboard.Focus(row);
                Task.Run(() => service.RaiseAgents(Agents("read c.cs"))).Wait();
                Pump(200);
                Assert.Same(row, Keyboard.FocusedElement);
                Assert.Equal("Scout1 scout · running · read c.cs", System.Windows.Automation.AutomationProperties.GetName(row));
            }, service, new FakeHost());
        }

        [Fact]
        public void Streaming_items_do_not_rebuild_an_unchanged_todo_list()
        {
            var todos = new[] { new TodoPhaseView { Name = "Plan", Tasks = new[] { new TodoTaskView { Content = "Write tests", Status = "in_progress" } } } };
            var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running, Todos = todos } };
            RunSta((window, control) =>
            {
                Descendants(window).OfType<ToggleButton>().First(t => t.IsVisible && System.Windows.Automation.AutomationProperties.GetName(t).StartsWith("todos 0/1")).IsChecked = true;
                Pump();
                ScrollViewer TodoScroller() => Descendants(window).OfType<ScrollViewer>().Single(s => s.IsVisible && s.MaxHeight == 220);
                var before = TodoScroller();
                Task.Run(() =>
                {
                    service.RaiseItem(new AssistantItem { Id = "a1", Text = "token", Streaming = true });
                    service.RaiseSession(new SessionView { Phase = SessionPhase.Running, Todos = todos, CostUsd = 0.01 });
                }).Wait();
                Pump(200);
                Assert.Same(before, TodoScroller());
            }, service, new FakeHost());
        }
    }
}
