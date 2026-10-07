using System.Text;
using Newtonsoft.Json.Linq;
using Omp.Core.Session;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Session;

public class ModelMapperTests
{
    private static JObject Wire() => new()
    {
        ["id"] = "claude-x", ["name"] = "Claude X", ["api"] = "anthropic-messages", ["provider"] = "anthropic", ["baseUrl"] = "https://example.invalid",
        ["reasoning"] = true, ["input"] = new JArray("text", "image"), ["cost"] = new JObject { ["input"] = 3, ["output"] = 15, ["cacheRead"] = 0.3, ["cacheWrite"] = 3.75 },
        ["contextWindow"] = 200000, ["maxTokens"] = 64000, ["thinking"] = new JObject { ["mode"] = "effort", ["efforts"] = new JArray("low", "medium", "high") },
    };

    [Fact]
    public void MapsIdentityLimitsModalitiesPricingAndThinkingEfforts()
    {
        var view = ModelMapper.ToModelView(Wire());
        Assert.Equal(("anthropic", "claude-x", "Claude X", "anthropic-messages", 200000L, 64000L, true), (view.Provider, view.Id, view.Name, view.Api, view.ContextWindow!.Value, view.MaxTokens!.Value, view.Reasoning));
        Assert.Equal(new[] { "low", "medium", "high" }, view.ThinkingEfforts);
        Assert.Equal(new[] { "text", "image" }, view.Input);
        Assert.Equal(3d, view.Cost!.Input);
        Assert.Equal(15d, view.Cost.Output);
    }

    [Fact]
    public void ToleratesNullLimitsAndMissingThinkingCostAndName()
    {
        var wire = Wire();
        wire.Remove("name");
        wire["contextWindow"] = null;
        wire["maxTokens"] = null;
        wire.Remove("thinking");
        wire.Remove("cost");
        wire.Remove("input");
        var view = ModelMapper.ToModelView(wire);
        Assert.Equal("claude-x", view.Name);
        Assert.Null(view.ContextWindow);
        Assert.Null(view.MaxTokens);
        Assert.Empty(view.ThinkingEfforts);
        Assert.Equal(new[] { "text" }, view.Input);
        Assert.Null(view.Cost);
    }

    [Fact]
    public void KeepsAMissingPriceFieldMissingInsteadOfZero()
    {
        var wire = Wire();
        wire["cost"] = new JObject { ["input"] = 3 };
        var view = ModelMapper.ToModelView(wire);
        Assert.Equal(3d, view.Cost!.Input);
        Assert.Null(view.Cost.Output);
    }
}

public sealed class SessionListerTests : IDisposable
{
    private readonly string _dir = TempDirectory.Create("omp-sessions-");

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string name, IEnumerable<object> lines, long mtimeSeconds)
    {
        var file = Path.Combine(_dir, name);
        File.WriteAllText(file, string.Join("\n", lines.Select(l => l is string s ? s : JToken.FromObject(l).ToString(Newtonsoft.Json.Formatting.None))) + "\n");
        File.SetLastWriteTimeUtc(file, DateTimeOffset.FromUnixTimeSeconds(mtimeSeconds).UtcDateTime);
        return file;
    }

    [Fact]
    public async Task ReadsHeaderTitleAndFirstUserMessageNewestFirst()
    {
        var older = Write("a.jsonl", new object[]
        {
            new { type = "title", v = 1, title = "", pad = "   " },
            new { type = "session", version = 3, id = "s-a", cwd = "/w", timestamp = "2026-01-01T00:00:00Z" },
            new { type = "model_change", id = "x" },
            new { type = "message", message = new { role = "assistant", content = Array.Empty<object>() } },
            new { type = "message", message = new { role = "user", content = new[] { new { type = "text", text = "first question" } } } },
            new { type = "message", message = new { role = "user", content = "second" } },
        }, 1000);
        var newer = Write("b.jsonl", new object[]
        {
            new { type = "title", v = 1, title = "Named session" },
            new { type = "session", id = "s-b", cwd = "/w", timestamp = "2026-01-02T00:00:00Z" },
            new { type = "message", message = new { role = "user", content = "hello" } },
        }, 2000);
        var sessions = await SessionLister.ListAsync(_dir, new MemoryLogger());
        Assert.Equal(
            new[] { $"{newer}|s-b|Named session|hello|/w|2000000", $"{older}|s-a||first question|/w|1000000" },
            sessions.Select(s => $"{s.Path}|{s.Id}|{s.Title}|{s.FirstMessage}|{s.Cwd}|{s.Modified}"));
        Assert.True(sessions[0].Size > 0);
    }

    [Fact]
    public async Task TruncatesALongFirstMessage()
    {
        Write("a.jsonl", new object[] { new { type = "session", id = "s" }, new { type = "message", message = new { role = "user", content = new string('x', 250) } } }, 1000);
        var session = Assert.Single(await SessionLister.ListAsync(_dir, new MemoryLogger()));
        Assert.Equal(new string('x', 200) + "…", session.FirstMessage);
    }

    [Fact]
    public async Task SkipsGarbageFilesNonJsonlFilesAndMalformedLines()
    {
        Write("garbage.jsonl", new object[] { "not json at all", "{\"type\":" }, 3000);
        Write("notes.txt", new object[] { new { type = "session", id = "nope" } }, 3000);
        var ok = Write("ok.jsonl", new object[] { "{broken", new { type = "session", id = "s-ok" } }, 1000);
        Directory.CreateDirectory(Path.Combine(_dir, "dir.jsonl"));
        var sessions = await SessionLister.ListAsync(_dir, new MemoryLogger());
        Assert.Equal(new[] { ok }, sessions.Select(s => s.Path));
    }

    [Fact]
    public async Task ReadsOnlyTheFirst64KiBOfEachFile()
    {
        Write("big.jsonl", new object[]
        {
            new { type = "session", id = "s-big" }, new { type = "custom", pad = new string('x', 70 * 1024) },
            new { type = "message", message = new { role = "user", content = "late" } },
        }, 1000);
        var session = Assert.Single(await SessionLister.ListAsync(_dir, new MemoryLogger()));
        Assert.Equal("s-big", session.Id);
        Assert.Null(session.FirstMessage);
    }

    [Fact]
    public async Task ReturnsAnEmptyListForAMissingDirectory()
    {
        Assert.Empty(await SessionLister.ListAsync(Path.Combine(_dir, "missing"), new MemoryLogger()));
    }

    [Fact]
    public async Task ListsTheDirectoryOffTheCallingThread()
    {
        using var release = new ManualResetEventSlim();
        var released = false;
        IEnumerable<string> Enumerate(string dir)
        {
            released = release.Wait(3000);
            return Directory.EnumerateFiles(dir);
        }
        var listing = SessionLister.ListAsync(_dir, new MemoryLogger(), enumerate: Enumerate);
        release.Set();
        await listing;
        Assert.True(released, "ListAsync enumerated the directory before returning to its caller");
    }

    [Fact]
    public async Task KeepsANonAsciiLastLineThatHasNoTrailingNewline()
    {
        File.WriteAllText(Path.Combine(_dir, "u.jsonl"), "{\"type\":\"session\",\"id\":\"s-u\"}\n{\"type\":\"title\",\"title\":\"héllo wörld ✓\"}", new UTF8Encoding(false));
        Assert.Equal("héllo wörld ✓", Assert.Single(await SessionLister.ListAsync(_dir, new MemoryLogger())).Title);
    }

    [Fact]
    public async Task SkipsAndLogsASessionFileThatCannotBeReadListingTheOthers()
    {
        Write("locked.jsonl", new object[] { new { type = "session", id = "s-locked" } }, 2000);
        var ok = Write("ok.jsonl", new object[] { new { type = "session", id = "s-ok" } }, 1000);
        var logger = new MemoryLogger();
        Task<SessionSummary?> Read(string file) => file.EndsWith("locked.jsonl")
            ? throw new IOException($"The process cannot access the file '{file}' because it is being used by another process.")
            : Task.FromResult<SessionSummary?>(new SessionSummary { Path = file, Modified = 1, Size = 1 });
        var sessions = await SessionLister.ListAsync(_dir, logger, Read);
        Assert.Equal(new[] { ok }, sessions.Select(s => s.Path));
        Assert.Matches(@"locked\.jsonl.*being used", logger.Text("warn"));
    }

    [Fact]
    public async Task ReadsAtMost16SessionFilesAtATime()
    {
        for (var i = 0; i < 100; i++) Write($"s{i}.jsonl", new object[] { new { type = "session", id = $"s{i}" } }, 1000 + i);
        var active = 0;
        var peak = 0;
        async Task<SessionSummary?> Read(string file)
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref peak, now);
            await Task.Delay(1);
            Interlocked.Decrement(ref active);
            return new SessionSummary { Path = file, Modified = 1, Size = 1 };
        }
        var sessions = await SessionLister.ListAsync(_dir, new MemoryLogger(), Read);
        Assert.Equal(100, sessions.Count);
        Assert.InRange(peak, 1, 16);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
