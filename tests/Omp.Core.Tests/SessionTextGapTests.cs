using Omp.Core.Session;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests;

public sealed class SessionTextGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omp-sessions-" + Guid.NewGuid().ToString("N"));

    public SessionTextGapTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task AUserMessageOfOnlyNonTextBlocksOrAnObjectGivesNoFirstMessage()
    {
        File.WriteAllLines(Path.Combine(_dir, "a.jsonl"),
        [
            "{\"type\":\"session\",\"id\":\"s\"}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"image\"}]}}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":{\"x\":1}}}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"then text\"}]}}",
        ]);
        var session = Assert.Single(await SessionLister.ListAsync(_dir, new MemoryLogger()));
        Assert.Equal("then text", session.FirstMessage);
    }

    [Fact]
    public async Task AFileThatVanishesIsSkippedSilentlyAndAnUnreadableOneIsLogged()
    {
        var logger = new MemoryLogger();
        var files = new[] { Path.Combine(_dir, "gone.jsonl"), Path.Combine(_dir, "locked.jsonl"), Path.Combine(_dir, "ok.jsonl") };
        var sessions = await SessionLister.ListAsync(_dir, logger,
            read: file => Path.GetFileName(file) switch
            {
                "gone.jsonl" => throw new FileNotFoundException(),
                "locked.jsonl" => throw new UnauthorizedAccessException("denied"),
                _ => Task.FromResult<SessionSummary?>(new SessionSummary { Path = file, Id = "ok" }),
            },
            enumerate: _ => files);
        Assert.Equal(new[] { "ok" }, sessions.Select(s => s.Id));
        Assert.Contains("Skipped unreadable session file", logger.Text("warn"));
        Assert.DoesNotContain("gone.jsonl", logger.Text("warn"));
    }
}
