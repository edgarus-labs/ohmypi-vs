using Omp.Core.Internal;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests;

public class ListenerTests
{
    [Fact]
    public void AFailingListenerIsLoggedAndTheOthersStillRun()
    {
        var logger = new MemoryLogger();
        var seen = new List<string>();
        Action<string>? many = v => throw new InvalidOperationException("boom " + v);
        many += seen.Add;
        Listeners.Raise(many, "event", "x", logger);
        Assert.Equal(new[] { "x" }, seen);
        Assert.Contains("OMP event listener failed", logger.Text("error"));

        Action? plain = () => throw new InvalidOperationException("plain boom");
        var ran = 0;
        plain += () => ran++;
        Listeners.Raise(plain, "tick", logger);
        Assert.Equal(1, ran);
        Assert.Contains("OMP tick listener failed", logger.Text("error"));
    }

    [Fact]
    public void RaisingNothingDoesNothing()
    {
        var logger = new MemoryLogger();
        Listeners.Raise((Action<string>?)null, "e", "x", logger);
        Listeners.Raise((Action?)null, "e", logger);
        Assert.Empty(logger.Records);
        Assert.True(Listeners.NowMs() > 0);
    }
}
