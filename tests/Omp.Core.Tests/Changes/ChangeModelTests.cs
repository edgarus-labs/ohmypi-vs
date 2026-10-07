using Newtonsoft.Json.Linq;
using Omp.Core.Changes;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Changes;

public class ChangeModelTests
{
    private static readonly string Cwd = Path.GetFullPath("D:\\work\\repo");
    private static readonly ChangeScope Scope = new() { Cwd = Cwd, Roots = new[] { Cwd } };

    private static string File(string name) => Path.Combine(Cwd, name);

    /// <summary>In-memory disk: absent keys are missing files; null values are untrackable; Reads lists every path read.</summary>
    private sealed class Disk
    {
        public Dictionary<string, Snapshot?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Reads { get; } = new();

        public Disk(params (string Path, string? Content)[] initial)
        {
            foreach (var (path, content) in initial)
            {
                Files[path] = content is null ? null : Snapshot.Of(content);
            }
        }

        public void Set(string path, string content) => Files[path] = Snapshot.Of(content);

        public Task<Snapshot?> Read(string path)
        {
            Reads.Add(path);

            return Task.FromResult(Files.TryGetValue(path, out var snapshot) ? snapshot : Snapshot.Missing);
        }
    }

    private static ToolExecutionEvent Start(string id, string name, JToken? args) =>
        new() { Phase = ToolExecutionPhase.Start, ToolCallId = id, Name = name, Args = args };

    private static ToolExecutionEvent End(string id, string name, JToken? args, JToken? details = null) =>
        new() { Phase = ToolExecutionPhase.End, ToolCallId = id, Name = name, Args = args, Result = new ToolResultView { Details = details } };

    private static JObject PathArg(string path) => new() { ["path"] = path };

    private static string Rows(ChangeModel model) =>
        string.Join("|", model.Changes.Select(c => $"{c.Path} {c.Status} +{c.Added} -{c.Removed}"));

    [Fact]
    public async Task TracksAFileCreatedByAToolAsAdded()
    {
        var disk = new Disk();
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "write", PathArg("new.ts")), Scope);
        disk.Set(File("new.ts"), "a\nb\n");
        var result = await model.ApplyAsync(End("t1", "write", PathArg("new.ts")), Scope);
        Assert.True(result.ChangesChanged);
        Assert.Equal($"{File("new.ts")} Added +2 -0", Rows(model));
        Assert.Same(Snapshot.Missing, model.Baseline(File("new.ts")));
    }

    [Fact]
    public async Task TracksAnEditedFileAsModifiedAgainstItsFirstSeenContent()
    {
        var disk = new Disk((File("a.ts"), "one\ntwo\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "one\nTWO\n");
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts")), Scope);
        await model.ApplyAsync(Start("t2", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "one\nTWO\nthree\n");
        await model.ApplyAsync(End("t2", "edit", PathArg("a.ts")), Scope);
        Assert.Equal($"{File("a.ts")} Modified +2 -1", Rows(model));
        Assert.Equal("one\ntwo\n", model.Baseline(File("a.ts"))!.Content);
        Assert.Equal(ChangeStatus.Modified, model.Change(File("A.TS"))!.Status);
    }

    [Fact]
    public async Task TracksARemovedFileAsDeleted()
    {
        var disk = new Disk((File("gone.ts"), "x\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "delete", PathArg("gone.ts")), Scope);
        disk.Files.Remove(File("gone.ts"));
        await model.ApplyAsync(End("t1", "delete", PathArg("gone.ts")), Scope);
        Assert.Equal($"{File("gone.ts")} Deleted +0 -1", Rows(model));
    }

    [Fact]
    public async Task DropsTheRowWhenTheFileIsBackAtItsBaseline()
    {
        var disk = new Disk((File("a.ts"), "same\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "changed\n");
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts")), Scope);
        await model.ApplyAsync(Start("t2", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "same\n");
        var result = await model.ApplyAsync(End("t2", "edit", PathArg("a.ts")), Scope);
        Assert.True(result.ChangesChanged);
        Assert.Empty(model.Changes);
    }

    [Fact]
    public async Task IgnoresReadOnlyTools()
    {
        var disk = new Disk((File("a.ts"), "x"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "read", PathArg("a.ts")), Scope);
        await model.ApplyAsync(End("t1", "read", PathArg("a.ts")), Scope);
        Assert.Empty(disk.Reads);
        Assert.Empty(model.Changes);
    }

    [Fact]
    public async Task ReplacesASnapshotTakenAfterTheWriteWithTheSameCallsReportedOldText()
    {
        var disk = new Disk((File("a.ts"), "after\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts"), new JObject { ["path"] = File("a.ts"), ["oldText"] = "before\n" }), Scope);
        Assert.Equal("before\n", model.Baseline(File("a.ts"))!.Content);
        Assert.Equal($"{File("a.ts")} Modified +1 -1", Rows(model));
    }

    [Fact]
    public async Task KeepsAnEarlierCallsBaselineOverALaterCallsOldText()
    {
        var disk = new Disk((File("a.ts"), "v1\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "v2\n");
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts")), Scope);
        await model.ApplyAsync(Start("t2", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "v3\n");
        await model.ApplyAsync(End("t2", "edit", PathArg("a.ts"), new JObject { ["path"] = File("a.ts"), ["oldText"] = "v2\n" }), Scope);
        Assert.Equal("v1\n", model.Baseline(File("a.ts"))!.Content);
    }

    [Fact]
    public async Task SkipsFilesThatCannotBeSnapshotted()
    {
        var disk = new Disk((File("big.bin"), null));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "write", PathArg("big.bin")), Scope);
        await model.ApplyAsync(End("t1", "write", PathArg("big.bin")), Scope);
        Assert.False(model.HasBaseline(File("big.bin")));
        Assert.Null(model.Baseline(File("big.bin")));
        Assert.Empty(model.Changes);
    }

    [Fact]
    public async Task ForgetsEverythingOnClearAndReturnsThePathsThatHadABaseline()
    {
        var disk = new Disk((File("a.ts"), "x"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "y");
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts")), Scope);
        model.Clear();
        Assert.Empty(model.Changes);
        Assert.False(model.HasBaseline(File("a.ts")));
    }

    [Fact]
    public async Task DoesNotReadOrTrackPathsOutsideTheWorkspaceRoots()
    {
        var outside = Path.GetFullPath("D:\\elsewhere\\secret.txt");
        const string unc = "\\\\attacker\\share\\x.txt";
        var disk = new Disk((outside, "s"), (File("in.ts"), "i"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "write", new JObject { ["paths"] = new JArray(outside, unc, "..\\escape.ts", "~/.ssh/config", "in.ts") }), Scope);
        await model.ApplyAsync(End("t1", "write", new JObject { ["paths"] = new JArray(outside, unc) }, new JObject { ["path"] = outside, ["oldText"] = "old" }), Scope);
        Assert.Equal(new[] { File("in.ts") }, disk.Reads);
        Assert.Empty(model.Changes);
        Assert.False(model.HasBaseline(outside));
    }

    [Fact]
    public async Task TracksPathsInsideAnyWorkspaceRoot()
    {
        var other = Path.GetFullPath("D:\\work\\other");
        var disk = new Disk((Path.Combine(other, "b.ts"), "b"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg(Path.Combine(other, "b.ts"))), new ChangeScope { Cwd = Cwd, Roots = new[] { Cwd, other } });
        Assert.True(model.HasBaseline(Path.Combine(other, "b.ts")));
    }

    [Fact]
    public async Task DoesNotTrackAReportedPathWhoseBaselineIsUnknown()
    {
        var disk = new Disk((File("real.ts"), "existing\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(Start("t1", "edit", PathArg("link.ts")), Scope);
        var result = await model.ApplyAsync(End("t1", "edit", PathArg("link.ts"), new JObject { ["resolvedPath"] = File("real.ts") }), Scope);
        Assert.False(result.ChangesChanged);
        Assert.Empty(model.Changes);
        Assert.False(model.HasBaseline(File("real.ts")));
    }

    [Fact]
    public async Task UsesAReportedOldTextAsTheBaselineOfAPathNotSnapshottedAtStart()
    {
        var disk = new Disk((File("real.ts"), "new\n"));
        var model = new ChangeModel(disk.Read, new MemoryLogger());
        await model.ApplyAsync(End("t1", "edit", new JObject(), new JObject { ["path"] = File("real.ts"), ["oldText"] = "old\n" }), Scope);
        Assert.Equal($"{File("real.ts")} Modified +1 -1", Rows(model));
    }

    private static async Task Settle(ChangeModel model, string id, string name)
    {
        await model.ApplyAsync(Start(id, "edit", PathArg(name)), Scope);
        await model.ApplyAsync(End(id, "edit", PathArg(name)), Scope);
    }

    [Fact]
    public async Task EvictsSettledBaselinesOfUnchangedFilesToMakeRoom()
    {
        var disk = new Disk((File("a.ts"), "aaaaaa"), (File("b.ts"), "bbbbbb"));
        var model = new ChangeModel(disk.Read, new MemoryLogger(), 10);
        await Settle(model, "t1", "a.ts");
        await model.ApplyAsync(Start("t2", "edit", PathArg("b.ts")), Scope);
        Assert.False(model.HasBaseline(File("a.ts")));
        Assert.Equal("bbbbbb", model.Baseline(File("b.ts"))!.Content);
    }

    [Fact]
    public async Task KeepsBaselinesOfChangedFilesAndStopsTrackingNewOnesWithAWarning()
    {
        var disk = new Disk((File("a.ts"), "aaaaaa"), (File("b.ts"), "bbbbbb"));
        var logger = new MemoryLogger();
        var model = new ChangeModel(disk.Read, logger, 10);
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        disk.Set(File("a.ts"), "changed");
        await model.ApplyAsync(End("t1", "edit", PathArg("a.ts")), Scope);
        await Settle(model, "t2", "b.ts");
        Assert.Equal("aaaaaa", model.Baseline(File("a.ts"))!.Content);
        Assert.False(model.HasBaseline(File("b.ts")));
        Assert.Equal(new[] { File("a.ts") }, model.Changes.Select(c => c.Path));
        Assert.Matches(@"b\.ts.*limit", logger.Text("warn"));
    }

    [Fact]
    public async Task KeepsTheBaselineOfAFileWhoseToolCallIsStillRunning()
    {
        var disk = new Disk((File("a.ts"), "aaaaaa"), (File("b.ts"), "bbbbbb"));
        var model = new ChangeModel(disk.Read, new MemoryLogger(), 10);
        await model.ApplyAsync(Start("t1", "edit", PathArg("a.ts")), Scope);
        await model.ApplyAsync(Start("t2", "edit", PathArg("b.ts")), Scope);
        Assert.Equal("aaaaaa", model.Baseline(File("a.ts"))!.Content);
        Assert.False(model.HasBaseline(File("b.ts")));
    }
}
