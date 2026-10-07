using Omp.Core.Processes;
using Omp.Core.Session;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests;

public class ExecutableResolverGapTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    [Fact]
    public void TheHomeVariableStandsInWhenTheUserProfileIsNotSet()
    {
        var found = ExecutableResolver.Locate(null, Env(("HOME", "C:\\home\\u")), path => path == "C:\\home\\u\\.local\\bin\\omp.exe");
        Assert.Equal("C:\\home\\u\\.local\\bin\\omp.exe", found);
    }

    [Fact]
    public void ADirectoryOnThePathThatCannotFormAFileNameIsSkipped()
    {
        var env = Env(("USERPROFILE", "C:\\Users\\u"), ("Path", "C:\\bad<dir;C:\\good"));
        Assert.Equal("C:\\good\\omp.exe", ExecutableResolver.Locate(null, env, path => path == "C:\\good\\omp.exe"));
    }

    [Fact]
    public void WithoutLocalAppDataTheSearchStillListsTheOtherLocations()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate(null, Env(("USERPROFILE", "C:\\Users\\u")), _ => false));
        Assert.Contains("C:\\Users\\u\\.bun\\bin", error.Message);
        Assert.DoesNotContain("AppData", error.Message);
    }
}

public sealed class SessionTextGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omp-sessions-" + Guid.NewGuid().ToString("N"));

    public SessionTextGapTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task AUserMessageOfOnlyNonTextBlocksOrAnObjectGivesNoFirstMessage()
    {
        File.WriteAllLines(Path.Combine(_dir, "a.jsonl"), new[]
        {
            "{\"type\":\"session\",\"id\":\"s\"}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"image\"}]}}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":{\"x\":1}}}",
            "{\"type\":\"message\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"then text\"}]}}",
        });
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
