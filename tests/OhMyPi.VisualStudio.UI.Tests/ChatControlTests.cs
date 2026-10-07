using Newtonsoft.Json.Linq;
using Omp.Core;
using Omp.Core.Changes;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>Hosts the real control in an off-screen WPF window and drives it through a fake service and host.</summary>
[Collection("wpf")]
public sealed class ChatControlTests
{
    [Fact]
    public void Renders_every_item_kind_and_coalesces_background_stream_updates()
    {
        var service = new FakeService
        {
            Session = new SessionView { SessionName = "Auth refactor", Phase = SessionPhase.Running, CostUsd = 0.0215, ContextUsage = new ContextUsageView { Percent = 13.2, Tokens = 13, ContextWindow = 100 } },
            Transcript = new TranscriptItem[]
            {
                new UserItem { Id = "u1", Text = "<editor-context>\nfile: a.cs\n</editor-context>\n\nFix the bug\n\nAttached: @src/a.cs" },
                new AssistantItem { Id = "a1", Text = "Done with **bold** and `code`:\n\n- one\n- two\n\n```cs\nvar x = 1;\n```", Thinking = "hmm\nthinking", Model = "M" },
                Tool("t1", "read", "{\"path\":\"C:\\\\repo\\\\src\\\\AuthService.cs\"}", ToolStatus.Done, new ToolResultView { Text = "x" }),
                Tool("t2", "grep", "{\"pattern\":\"ValidateToken\",\"path\":\"src\"}", ToolStatus.Done, new ToolResultView { Text = "a\nb", Details = JToken.Parse("{\"matchCount\":8}") }),
                Tool("t3", "bash", "{\"command\":\"npm test\"}", ToolStatus.Error, new ToolResultView { Text = "npm ERR! test failed", IsError = true, Details = JToken.Parse("{\"exitCode\":1}") }),
                new NoticeItem { Id = "n1", Level = NoticeLevel.Warning, Text = "Heads up" },
                new CommandOutputItem { Id = "c1", Text = "command output" },
            },
        };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "Auth refactor"));
            Assert.True(HasText(window, "ctx 13% · $0.0215"));
            Assert.True(HasText(window, "working"));
            Assert.True(HasText(window, "Fix the bug"));
            Assert.True(HasText(window, "editor context"));
            Assert.True(HasText(window, "@src/a.cs"));
            Assert.True(HasText(window, "Thinking · 2 lines"));
            Assert.True(HasText(window, "src\\AuthService.cs"));
            Assert.True(HasText(window, "\"ValidateToken\" in src"));
            Assert.True(HasText(window, "8 hits · 12ms"));
            Assert.True(HasText(window, "npm ERR! test failed"));
            Assert.True(HasText(window, "Heads up"));
            Assert.True(HasText(window, "command output"));
            Assert.Contains(Descendants(window).OfType<TextBox>(), t => t.Text == "var x = 1;");

            var worker = Task.Run(() =>
            {
                for (var i = 1; i <= 300; i++)
                {
                    service.RaiseItem(new AssistantItem { Id = "stream", Text = "token " + i, Streaming = i < 300 });
                }
            });
            Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
            Pump(300);
            Assert.True(HasText(window, "token 300"));
            Assert.False(HasText(window, "token 299"));
        }, service, host);
    }

    [Fact]
    public void Enter_sends_the_text_with_file_mentions_and_no_editor_context()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            control.AddFileMentions(new[] { "C:\\repo\\src\\a.cs" });
            Pump();
            Assert.True(HasText(window, "a.cs"));
            var input = Named<TextBox>(window, "Prompt");
            input.Text = "line one";
            Keyboard.Focus(input);
            Assert.Empty(service.Prompts);
            Press(input, Key.Enter);
            Pump(200);
            var prompt = Assert.Single(service.Prompts);
            Assert.Equal("line one\n\nAttached: @src\\a.cs", prompt.Text);
            Assert.Equal(PromptMode.Auto, prompt.Mode);
            Assert.Equal("", input.Text);
            Assert.DoesNotContain(Descendants(window).OfType<CheckBox>(), c => c.IsVisible);
        }, service, new FakeHost());
    }

    [Fact]
    public void Plus_adds_the_active_editor_file_and_is_disabled_without_one()
    {
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            var plus = Named<Button>(window, "Add the active editor file");
            Assert.False(plus.IsEnabled);
            host.SetActiveDocument("C:\\repo\\src\\Auth.cs");
            Pump();
            Assert.True(plus.IsEnabled);
            plus.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            Assert.True(HasText(window, "Auth.cs"));
            host.SetActiveDocument("C:\\repo\\src\\Token.cs");
            Pump();
            plus.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            Assert.True(HasText(window, "Token.cs"));
            host.SetActiveDocument(null);
            Pump();
            Assert.False(plus.IsEnabled);
        }, new FakeService(), host);
    }

    [Fact]
    public void Busy_session_offers_one_stop_button_and_enter_steers_the_running_turn()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running } };
        RunSta((window, control) =>
        {
            Assert.DoesNotContain(Descendants(window).OfType<Button>(), b => b.IsVisible && (System.Windows.Automation.AutomationProperties.GetName(b) is "Follow-up" or "Steer"));
            var input = Named<TextBox>(window, "Prompt");
            input.Text = "focus on tests";
            Keyboard.Focus(input);
            Press(input, Key.Enter);
            Pump(200);
            Assert.Equal(PromptMode.Auto, Assert.Single(service.Prompts).Mode);
            Click(Named<Button>(window, "Stop"));
            Pump(200);
            Assert.Equal(1, service.Aborts);
        }, service, new FakeHost());
    }

    [Fact]
    public void Escape_stops_a_busy_turn_from_the_prompt_or_the_transcript_and_does_nothing_when_idle()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Yielded } };
        RunSta((window, control) =>
        {
            var input = Named<TextBox>(window, "Prompt");
            Keyboard.Focus(input);
            Press(input, Key.Escape);
            Pump(200);
            Assert.Equal(1, service.Aborts);
            Press(Named<FrameworkElement>(window, "Conversation"), Key.Escape);
            Pump(200);
            Assert.Equal(2, service.Aborts);
            Task.Run(() => service.RaiseSession(new SessionView { Phase = SessionPhase.Idle })).Wait();
            Pump(200);
            Press(input, Key.Escape);
            Pump(200);
            Assert.Equal(2, service.Aborts);
        }, service, new FakeHost());
    }

    [Fact]
    public void Refused_prompt_restores_the_draft_and_shows_the_error()
    {
        var service = new FakeService { PromptRefusal = "OMP is busy" };
        RunSta((window, control) =>
        {
            var input = Named<TextBox>(window, "Prompt");
            input.Text = "hello";
            Press(input, Key.Enter);
            Pump(300);
            Assert.Equal("hello", input.Text);
            Assert.True(HasText(window, "Prompt failed: OMP is busy"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Interactions_are_answered_only_by_the_user_and_removed_when_cancelled()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            Task.Run(() =>
            {
                service.RaiseInteraction(new ConfirmRequest { Id = "c1", Title = "Run rm -rf build?", Message = "bash" });
                service.RaiseInteraction(new InputRequest { Id = "s1", Title = "API key", Secret = true });
                service.RaiseInteraction(new SelectRequest { Id = "x1", Title = "Pick one", Options = new[] { new SelectOptionView { Label = "Alpha" } } });
            }).Wait();
            Pump(200);
            Assert.True(HasText(window, "Run rm -rf build?"));
            Assert.True(HasText(window, "waiting"));
            Assert.Empty(service.Responses);
            Assert.Contains(Descendants(window).OfType<PasswordBox>(), p => p.IsVisible);

            Click(Named<Button>(window, "Approve"));
            Pump();
            var confirm = Assert.Single(service.Responses);
            Assert.Equal("c1", confirm.Id);
            Assert.True(Assert.IsType<ConfirmedResponse>(confirm.Response).Confirmed);
            Assert.False(HasText(window, "Run rm -rf build?"));

            var password = Descendants(window).OfType<PasswordBox>().First(p => p.IsVisible);
            password.Password = "s3cret";
            Press(password, Key.Enter);
            Pump();
            Assert.Equal("s3cret", Assert.IsType<ValueResponse>(service.Responses[1].Response).Value);
            Assert.Equal("", password.Password);

            Task.Run(() => service.RaiseInteractionCancelled("x1")).Wait();
            Pump(200);
            Assert.False(HasText(window, "Pick one"));
            Assert.Equal(2, service.Responses.Count);
        }, service, new FakeHost());
    }

    [Fact]
    public void Agents_and_changes_sections_render_rows_and_open_diffs()
    {
        var service = new FakeService
        {
            Agents = new[]
            {
                new AgentView { Id = "main", Name = "main", Status = AgentStatus.Running },
                new AgentView { Id = "ReviewAuth", Name = "scout", Status = AgentStatus.Running, Activity = "read src/auth.ts", ParentId = "main" },
            },
        };
        var host = new FakeHost { Changes = new[] { new TrackedChange { Path = "C:\\repo\\src\\auth\\AuthService.cs", Status = ChangeStatus.Modified, Added = 3, Removed = 1 } } };
        RunSta((window, control) =>
        {
            control.ShowAgents();
            Pump();
            Assert.True(HasText(window, "Agents 2 · 2 running"));
            Assert.True(HasText(window, "scout · running · read src/auth.ts"));
            Assert.True(HasText(window, "Changes · 1 file"));

            host.Changes = host.Changes.Concat(new[] { new TrackedChange { Path = "C:\\repo\\New.cs", Status = ChangeStatus.Added, Added = 4 } }).ToList();
            Task.Run(() => host.RaiseChanges()).Wait();
            Pump(200);
            Assert.True(HasText(window, "Changes · 2 files"));

            Named<ToggleButton>(window, "Changes · 2 files").IsChecked = true;
            Pump();
            Click(Named<Button>(window, "Diff AuthService.cs modified"));
            Pump();
            Assert.Equal("C:\\repo\\src\\auth\\AuthService.cs", Assert.Single(host.Diffs));
        }, service, host);
    }

    [Fact]
    public void Missing_executable_shows_the_banner_and_disables_the_input()
    {
        var service = new FakeService { Connection = new ConnectionStatus { State = ConnectionState.Stopped } };
        var host = new FakeHost { Unavailable = new OmpUnavailable("omp is not on PATH", executableNotFound: true) };
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "OMP not found: omp is not on PATH"));
            Assert.True(HasText(window, "offline"));
            Assert.False(Named<TextBox>(window, "Prompt").IsEnabled);
        }, service, host);
    }

    [Fact]
    public void A_service_that_could_not_be_built_for_another_reason_is_reported_as_not_available()
    {
        var service = new FakeService { Connection = new ConnectionStatus { State = ConnectionState.Stopped } };
        var host = new FakeHost { Unavailable = new OmpUnavailable("Access to the path is denied", executableNotFound: false) };
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "OMP is not available: Access to the path is denied"));
            Assert.False(HasText(window, "OMP not found: Access to the path is denied"));
            Assert.False(Named<TextBox>(window, "Prompt").IsEnabled);
        }, service, host);
    }

    [Fact]
    public void Session_history_lists_sessions_and_escape_closes_it()
    {
        var service = new FakeService
        {
            Connection = new ConnectionStatus { State = ConnectionState.Ready },
            Sessions = new[] { new SessionSummary { Path = "C:\\s\\a.jsonl", Title = "Debug VPN", Modified = 1, Size = 10 } },
        };
        RunSta((window, control) =>
        {
            control.ShowSessionPicker();
            Pump(300);
            var row = Descendants(window).OfType<TextBlock>().FirstOrDefault(t => t.IsVisible && t.Text == "Debug VPN" && IsInside(t, "Session history"));
            Assert.NotNull(row);
            var history = Descendants(window).OfType<FrameworkElement>().First(e => e.IsVisible && System.Windows.Automation.AutomationProperties.GetName(e) == "Session history");

            Press(Named<TextBox>(window, "Search sessions"), Key.Escape);
            Pump();
            Assert.False(history.IsVisible);
        }, service, new FakeHost());
    }

    [Fact]
    public void File_references_in_assistant_text_open_the_file_at_the_line()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omp-ui-links-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "src"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "src", "A.cs"), "class A {}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(root, "B.md"), "# B");
        try
        {
            var service = new FakeService
            {
                Cwd = root,
                Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = "See `src/A.cs:3`, [notes](B.md) and `src/Missing.cs`." } },
            };
            var host = new FakeHost();
            RunSta((window, control) =>
            {
                var links = Hyperlinks(window).ToList();
                Assert.Equal(2, links.Count);
                foreach (var link in links)
                {
                    link.DoClick();
                }

                Pump();
                Assert.Equal(new (string, int?)[] { (System.IO.Path.Combine(root, "src", "A.cs"), 3), (System.IO.Path.Combine(root, "B.md"), null) }, host.Opened);
            }, service, host);
        }
        finally
        {
            System.IO.Directory.Delete(root, true);
        }
    }

    private static bool IsInside(DependencyObject element, string automationName)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement fe && System.Windows.Automation.AutomationProperties.GetName(fe) == automationName)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Shows_what_the_agent_is_doing_below_the_conversation_and_the_turn_summary_when_done()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new UserItem { Id = "u1", Text = "go" } } };
        RunSta((window, control) =>
        {
            Assert.False(HasText(window, "Working…"));
            service.Session = new SessionView { Phase = SessionPhase.Running };
            service.RaiseSession(service.Session);
            Pump();
            Assert.True(HasText(window, "Working…"));

            service.RaiseItem(new AssistantItem { Id = "a1", Text = "done it" });
            service.RaiseItem(new TurnSummaryItem { Id = "t1", DurationMs = 72_400, InputTokens = 1200, OutputTokens = 350, CostUsd = 0.0412 });
            service.Session = new SessionView { Phase = SessionPhase.Idle };
            service.RaiseSession(service.Session);
            Pump();
            Assert.False(HasText(window, "Working…"));
            Assert.True(HasText(window, "Whole turn · 1m 12s · 1.2k in · 350 out · $0.0412"));
        }, service, new FakeHost());
    }

    [Fact]
    public void Typing_a_slash_offers_omp_commands_and_tab_completes_one_then_enter_sends_it()
    {
        var service = new FakeService();
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            service.RaiseCommands(new[]
            {
                new SlashCommandView { Name = "model", Description = "Show current model selection" },
                new SlashCommandView { Name = "compact", Description = "Compact the context" },
            });
            Pump();
            var input = Named<TextBox>(window, "Prompt");
            input.Focus();
            input.Text = "/co";
            input.CaretIndex = input.Text.Length;
            Pump();
            var offered = PresentationSource.CurrentSources.OfType<PresentationSource>()
                .Where(s => s.RootVisual != null && !ReferenceEquals(s.RootVisual, window))
                .SelectMany(s => Descendants(s.RootVisual))
                .OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains("/compact", offered);
            Assert.DoesNotContain("/model", offered);
            Press(input, Key.Tab);
            Pump();
            Assert.Equal("/compact ", input.Text);
            Press(input, Key.Enter);
            Pump(300);
            Assert.Equal("/compact", Assert.Single(service.Prompts).Text);
        }, service, host);
    }
}
