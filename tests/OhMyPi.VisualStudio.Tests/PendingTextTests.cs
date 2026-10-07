using OhMyPi.VisualStudio.Logic;
using System;

namespace OhMyPi.VisualStudio.Tests;

public sealed class PendingTextTests
{
    [Fact]
    public void OnlyTheFirstLineAfterADrainAsksForAFlush()
    {
        var pending = new PendingText();
        Assert.True(pending.Append("one"));
        Assert.False(pending.Append("two"));
        Assert.Equal("one" + Environment.NewLine + "two" + Environment.NewLine, pending.Drain());
        Assert.True(pending.Append("three"));
        Assert.Equal("three" + Environment.NewLine, pending.Drain());
    }
}
