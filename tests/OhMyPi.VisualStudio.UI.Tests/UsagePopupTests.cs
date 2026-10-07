using Omp.Core;
using System;
using System.Linq;
using System.Windows.Controls;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The usage popup: what it shows for OMP's report, refreshing, failures and closing.</summary>
[Collection("wpf")]
public sealed class UsagePopupTests
{
    private static readonly ProviderUsage[] Report =
    [
        new ProviderUsage
        {
            Provider = "Anthropic",
            Limits = new[]
            {
                new UsageLimit { Name = "Claude 5 Hour", Account = "user@example.com", UsedPercent = 4, Resets = "in 4h", InUse = true },
                new UsageLimit { Name = "Claude 7 Day", Account = "user@example.com", UsedPercent = 85 },
            },
        },
        new ProviderUsage { Provider = "Openai Codex", Limits = new[] { new UsageLimit { Name = "5 hours", Account = "team", UsedPercent = 120, Resets = "in 2h" } } },
        new ProviderUsage { Provider = "Xai" },
    ];

    /// <summary>The usage popup's content while the popup is open; null when it is closed.</summary>
    private static System.Windows.FrameworkElement? Shown(System.Windows.Window window) =>
        System.Windows.PresentationSource.CurrentSources.OfType<System.Windows.PresentationSource>()
            .Select(source => source.RootVisual).Where(root => root != null && !ReferenceEquals(root, window))
            .SelectMany(root => Descendants(root!)).OfType<System.Windows.FrameworkElement>()
            .FirstOrDefault(element => element.IsVisible && System.Windows.Automation.AutomationProperties.GetName(element) == "Usage");

    private static System.Windows.FrameworkElement UsagePopup(System.Windows.Window window) => Shown(window) ?? throw new Xunit.Sdk.XunitException("the usage popup is closed");

    private static void Open(OmpChatControl control)
    {
        control.ShowUsage();
        Pump(200);
    }

    [Fact]
    public void Opening_shows_each_provider_reporting_limits_with_the_share_used_and_reset()
    {
        var service = new FakeService { Usage = Report };
        RunSta((window, control) =>
        {
            Open(control);
            var popup = UsagePopup(window);
            var texts = Texts(popup).ToList();
            Assert.Contains("ANTHROPIC", texts);
            Assert.Contains("OPENAI CODEX", texts);
            Assert.DoesNotContain("XAI", texts);
            Assert.Contains("Claude 5 Hour", texts);
            Assert.Contains("4%", texts);
            Assert.Contains("in 4h", texts);
            Assert.Contains("85%", texts);
            Assert.Contains("100%", texts);
            var row = Descendants(popup).OfType<Grid>().First(grid => System.Windows.Automation.AutomationProperties.GetName(grid) == "Claude 5 Hour 4% used");
            Assert.Equal("Claude 5 Hour\nuser@example.com\n4% used\nResets in 4h\nUsed by this session", row.ToolTip);
            var other = Descendants(popup).OfType<Grid>().First(grid => System.Windows.Automation.AutomationProperties.GetName(grid) == "Claude 7 Day 85% used");
            Assert.Equal("Claude 7 Day\nuser@example.com\n85% used", other.ToolTip);
        }, service, new FakeHost());
    }

    [Fact]
    public void Each_opening_and_refresh_asks_omp_again()
    {
        var service = new FakeService { Usage = Report };
        RunSta((window, control) =>
        {
            Open(control);
            Assert.Equal(1, service.UsageRequests);
            Click(Named<Button>(UsagePopup(window), "Refresh usage"));
            Pump();
            Assert.Equal(2, service.UsageRequests);
        }, service, new FakeHost());
    }

    [Fact]
    public void A_report_without_limits_says_so()
    {
        var service = new FakeService { Usage = new[] { new ProviderUsage { Provider = "Xai" } } };
        RunSta((window, control) =>
        {
            Open(control);
            Assert.True(HasText(UsagePopup(window), "No provider reports usage limits."));
        }, service, new FakeHost());
    }

    [Fact]
    public void A_failed_request_is_shown_and_logged()
    {
        var service = new FakeService { UsageError = new InvalidOperationException("OMP printed no usage report") };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Open(control);
            Assert.True(HasText(UsagePopup(window), "Usage is unavailable: OMP printed no usage report"));
            Assert.Contains(host.Errors, error => error.Message == "Loading usage from OMP failed");
        }, service, host);
    }

    [Fact]
    public void The_command_toggles_the_popup_and_the_close_button_closes_it()
    {
        var service = new FakeService { Usage = Report };
        RunSta((window, control) =>
        {
            Open(control);
            Assert.NotNull(Shown(window));
            Open(control);
            Assert.Null(Shown(window));

            Open(control);
            Assert.Null(Shown(window));
            System.Threading.Thread.Sleep(450);
            Open(control);
            Click(Named<Button>(UsagePopup(window), "Close usage"));
            Pump();
            Assert.Null(Shown(window));
        }, service, new FakeHost());
    }
}
