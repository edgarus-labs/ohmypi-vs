using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public class SessionMemoryTests
{
    private readonly SessionMemory _memory = new();

    [Fact]
    public void RemembersNewSessionsOfTheCurrentDirectory()
    {
        _memory.SwitchTo(@"D:\a", @"C:\s\old.jsonl");
        Assert.True(_memory.Record(@"D:\a\", @"C:\s\new.jsonl"));
        Assert.Equal(@"C:\s\new.jsonl", _memory.Last);
        Assert.False(_memory.Record(@"D:\a", @"C:\s\new.jsonl"));
        Assert.False(_memory.Record(@"D:\a", ""));
    }

    [Fact]
    public void ASessionOfTheServiceBeingReplacedIsPersistedForItsOwnDirectoryOnly()
    {
        _memory.SwitchTo(@"D:\b", null);
        Assert.True(_memory.Record(@"D:\a", @"C:\s\a.jsonl"));
        Assert.Null(_memory.Last);
    }

    [Fact]
    public void SwitchingForgetsThePreviousDirectorysSession()
    {
        _memory.SwitchTo(@"D:\a", @"C:\s\a.jsonl");
        _memory.SwitchTo(@"D:\b", "");
        Assert.Null(_memory.Last);
    }
}
