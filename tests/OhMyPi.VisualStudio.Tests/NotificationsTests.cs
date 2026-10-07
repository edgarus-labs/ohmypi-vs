using OhMyPi.VisualStudio.Logic;
using System;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Tests;

public sealed class NotificationsTests
{
    [Fact]
    public void ShortSingleLineMessagesAreShownAsTheyAre() => Assert.Equal("OMP failed to start: spawn ENOENT", NotificationText.Summary("OMP failed to start: spawn ENOENT"));

    [Fact]
    public void MultiLineMessagesShowTheirFirstLine()
    {
        var message = "oh-my-pi: Could not find the omp executable. Tried:\n  C:\\a\\omp.exe\n  C:\\a\\omp.cmd";
        Assert.Equal("oh-my-pi: Could not find the omp executable. Tried: … (details in the oh-my-pi log)", NotificationText.Summary(message));
    }

    [Fact]
    public void LongLinesAreCut()
    {
        var summary = NotificationText.Summary("OMP stopped: " + new string('x', 500));
        Assert.True(summary.Length <= NotificationText.MaxLength, summary);
        Assert.EndsWith("… (details in the oh-my-pi log)", summary);
        Assert.StartsWith("OMP stopped: xxx", summary);
    }

    [Fact]
    public async Task ARepeatedMessageReusesTheOpenNotification()
    {
        var open = new OpenNotifications();
        var shown = 0;
        var choice = new TaskCompletionSource<string?>();
        Task<string?> Show()
        {
            shown++;

            return choice.Task;
        }

        var first = open.ShowOnceAsync("OMP stopped", Show);
        var second = open.ShowOnceAsync("OMP stopped", Show);
        Assert.Equal(1, shown);
        choice.SetResult("Restart");
        Assert.Equal("Restart", await first);
        Assert.Equal("Restart", await second);

        choice = new TaskCompletionSource<string?>();
        var third = open.ShowOnceAsync("OMP stopped", Show);
        Assert.Equal(2, shown);
        Assert.False(third.IsCompleted);
    }

    [Fact]
    public async Task DifferentMessagesAreShownSeparately()
    {
        var open = new OpenNotifications();
        var shown = 0;
        Task<string?> Show()
        {
            shown++;

            return Task.FromResult<string?>(null);
        }

        await open.ShowOnceAsync("a", Show);
        await open.ShowOnceAsync("b", Show);
        Assert.Equal(2, shown);
    }

    [Fact]
    public async Task AFailedNotificationIsForgotten()
    {
        var open = new OpenNotifications();
        await Assert.ThrowsAsync<InvalidOperationException>(() => open.ShowOnceAsync("a", () => Task.FromException<string?>(new InvalidOperationException("no shell"))));
        Assert.Equal("ok", await open.ShowOnceAsync("a", () => Task.FromResult<string?>("ok")));
    }
}
