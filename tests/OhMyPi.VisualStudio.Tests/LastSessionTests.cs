using OhMyPi.VisualStudio.Logic;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class LastSessionTests
{
    [Fact]
    public void KeyIsStablePerDirectoryIgnoringCaseAndTrailingSeparator()
    {
        var key = LastSession.KeyFor(@"D:\Work\Repo\");
        Assert.Equal(key, LastSession.KeyFor(@"d:\work\repo"));
        Assert.NotEqual(key, LastSession.KeyFor(@"D:\work\repo2"));
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void ResumesThePreferredFileWhileItExists()
    {
        var existing = new HashSet<string> { "a.jsonl", "last.jsonl" };
        var options = LastSession.ResumeOptions("a.jsonl", "last.jsonl", existing.Contains, null);
        Assert.Equal("a.jsonl", options!.ResumeSessionFile);
        Assert.False(options.NewSession);
    }

    [Fact]
    public void ResumesTheLastFileOtherwise()
    {
        var existing = new HashSet<string> { "last.jsonl" };
        Assert.Equal("last.jsonl", LastSession.ResumeOptions("gone.jsonl", "last.jsonl", existing.Contains, null)!.ResumeSessionFile);
    }

    [Fact]
    public void StartsUnboundWhenTheLastFileIsGone()
    {
        var logged = new List<string>();
        Assert.Null(LastSession.ResumeOptions(null, "gone.jsonl", _ => false, logged.Add));
        Assert.Null(LastSession.ResumeOptions(null, null, _ => true, logged.Add));
        Assert.Equal(new[] { "Last session file no longer exists: gone.jsonl" }, logged);
    }
}
