using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>Binding, teardown, service start, failure reporting and OMP-initiated requests of the chat control.</summary>
[Collection("wpf")]
public sealed class ChatControlLifecycleTests
{
    [Fact]
    public void Dispose_detaches_every_service_and_host_handler()
    {
        var service = new FakeService();
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Assert.True(service.HandlerCount > 0);
            Assert.True(host.HandlerCount > 0);
            control.Dispose();
            Assert.Equal(0, service.HandlerCount);
            Assert.Equal(0, host.HandlerCount);
        }, service, host);
    }

    [Fact]
    public void A_disposed_control_is_collectable_while_its_service_and_host_live_on()
    {
        var service = new FakeService
        {
            Session = new SessionView { Phase = SessionPhase.Running },
            PendingInteractions = new InteractionRequest[] { new ConfirmRequest { Id = "c1", Title = "Deploy?", TimeoutMs = 60_000 } },
        };
        var host = new FakeHost();
        RunStaWindow(window =>
        {
            var weak = ShowAndDispose(window, service, host);
            for (var i = 0; i < 3; i++)
            {
                Pump(50);
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(weak.IsAlive, "the disposed chat control is still reachable");
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndDispose(Window window, FakeService service, FakeHost host)
    {
        var control = new OmpChatControl();
        window.Content = control;
        control.Initialize(service, host);
        Pump();
        control.Dispose();
        window.Content = null;

        return new WeakReference(control);
    }

    [Fact]
    public void A_prompt_waits_for_omp_to_finish_starting()
    {
        var gate = new TaskCompletionSource<bool>();
        var service = new FakeService { Connection = new ConnectionStatus { State = ConnectionState.Starting } };
        var host = new FakeHost { EnsureGate = gate };
        RunSta((window, control) =>
        {
            var input = Named<TextBox>(window, "Prompt");
            input.Text = "hello";
            Press(input, Key.Enter);
            Pump(200);
            Assert.Equal(1, host.EnsureCalls);
            Assert.Empty(service.Prompts);
            gate.SetResult(true);
            Pump(200);
            Assert.Equal("hello", Assert.Single(service.Prompts).Text);
        }, service, host);
    }

    [Fact]
    public void Pickers_and_fast_mode_wait_for_omp_before_calling_it()
    {
        var service = new FakeService { Connection = new ConnectionStatus { State = ConnectionState.Restarting } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            control.ShowModelPicker();
            Pump(200);
            Assert.Equal(1, host.EnsureCalls);
            control.ShowSessionPicker();
            Pump(200);
            Assert.Equal(2, host.EnsureCalls);
            Press(Named<TextBox>(window, "Search sessions"), Key.Escape);
            Pump();
            Click(Named<ToggleButton>(window, "Fast mode"));
            Pump(200);
            Assert.Equal(3, host.EnsureCalls);
        }, service, host);
    }

    [Fact]
    public void Interactions_pending_before_the_control_existed_are_shown_once()
    {
        var request = new InputRequest { Id = "i1", Title = "Branch name?" };
        var service = new FakeService { PendingInteractions = new InteractionRequest[] { request } };
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "Branch name?"));
            Assert.True(HasText(window, "waiting"));
            var field = Named<TextBox>(window, "Branch name?");
            field.Text = "feature/x";
            Task.Run(() => service.RaiseInteraction(request)).Wait();
            Pump(200);
            Assert.Single(Texts(window), t => t == "Branch name?");
            Assert.Equal("feature/x", Named<TextBox>(window, "Branch name?").Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_failed_answer_keeps_the_card_and_the_typed_secret_and_reports_the_error()
    {
        var service = new FakeService { RespondError = new InvalidOperationException("OMP is not running") };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Task.Run(() =>
            {
                service.RaiseInteraction(new ConfirmRequest { Id = "c1", Title = "Run it?" });
                service.RaiseInteraction(new InputRequest { Id = "s1", Title = "API key", Secret = true });
            }).Wait();
            Pump(200);
            Click(Named<Button>(window, "Approve"));
            Pump();
            Assert.True(HasText(window, "Run it?"));
            Assert.True(HasText(window, "Answering OMP failed: OMP is not running"));

            var password = Descendants(window).OfType<PasswordBox>().First(p => p.IsVisible);
            password.Password = "s3cret";
            Press(password, Key.Enter);
            Pump();
            Assert.Equal("s3cret", password.Password);
            Assert.Equal(2, host.Errors.Count);
            Assert.All(host.Errors, e => Assert.Equal("OMP is not running", e.Error.Message));
        }, service, host);
    }

    [Fact]
    public void Failed_actions_and_render_failures_are_logged_with_their_exception()
    {
        var service = new FakeService
        {
            ListModelsError = new InvalidOperationException("no providers"),
            Transcript = new TranscriptItem[] { new AssistantItem { Id = "a1", Text = null! } },
        };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "Failed to render AssistantItem"));
            Assert.Contains(host.Errors, e => e.Message.Contains("AssistantItem") && e.Error is NullReferenceException);
            control.ShowModelPicker();
            Pump(300);
            Assert.Contains(host.Errors, e => e.Message == "Select model failed" && e.Error.Message == "no providers");
        }, service, host);
    }

    [Fact]
    public void A_failing_coalesced_update_becomes_a_logged_notice()
    {
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            host.ChangesError = new InvalidOperationException("changes unavailable");
            Task.Run(() => host.RaiseChanges()).Wait();
            Pump(300);
            Assert.True(HasText(window, "Updating the chat failed: changes unavailable"));
            Assert.Contains(host.Errors, e => e.Error.Message == "changes unavailable");
        }, new FakeService(), host);
    }

    [Fact]
    public void Commands_updated_while_the_control_is_built_are_offered()
    {
        var service = new FakeService();
        service.CommandsRead = () => service.RaiseCommands(new[] { new SlashCommandView { Name = "compact", Description = "Compact the context" } });
        RunSta((window, control) =>
        {
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
        }, service, new FakeHost());
    }

    [Fact]
    public void Omp_open_url_requests_launch_only_web_urls_and_only_after_the_user_confirms()
    {
        var service = new FakeService();
        RunSta((window, control) =>
        {
            var launched = new List<string>();
            control.LaunchUrl = launched.Add;
            Task.Run(() => service.RaisePresentation(new OpenUrlPresentation { Url = "ms-msdt:/id PCWDiagnostic" })).Wait();
            Pump(200);
            Assert.True(HasText(window, "OMP asked to open a non-web URL; ignored: ms-msdt:/id PCWDiagnostic"));
            Assert.Empty(AllNamed<Button>(window, "Open"));

            Task.Run(() => service.RaisePresentation(new OpenUrlPresentation { Url = "https://example.com/a b", Instructions = "Sign in" })).Wait();
            Pump(200);
            Assert.True(HasText(window, "OMP wants to open a URL in your browser."));
            Assert.Empty(launched);
            Click(Named<Button>(window, "Open"));
            Pump();
            Assert.Equal(new[] { "https://example.com/a%20b" }, launched);
            Assert.False(HasText(window, "OMP wants to open a URL in your browser."));
        }, service, new FakeHost());
    }

    [Fact]
    public void Expand_state_does_not_carry_over_to_another_session()
    {
        var service = new FakeService { Transcript = new TranscriptItem[] { new AssistantItem { Id = "h0", Text = "answer", Thinking = "plan" } } };
        RunSta((window, control) =>
        {
            Named<ToggleButton>(window, "Thinking · 1 line").IsChecked = true;
            Pump();
            Task.Run(() => service.RaiseReset(new TranscriptItem[] { new AssistantItem { Id = "h0", Text = "other answer", Thinking = "other plan" } })).Wait();
            Pump(200);
            Assert.True(HasText(window, "other answer"));
            Assert.False(Named<ToggleButton>(window, "Thinking · 1 line").IsChecked);
        }, service, new FakeHost());
    }
}
