using Omp.Core.Changes;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests.Changes;

public sealed class ReadSnapshotTests : IDisposable
{
    private readonly string _dir = TempDirectory.Create("omp-snapshot-");

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task ReadsAFileAndReportsAMissingOneAsMissing()
    {
        var logger = new MemoryLogger();
        System.IO.File.WriteAllText(Path.Combine(_dir, "a.txt"), "hello\u00e9");
        var snapshot = await ChangeModel.ReadSnapshotAsync(Path.Combine(_dir, "a.txt"), logger);
        Assert.True(snapshot!.Exists);
        Assert.Equal("hello\u00e9", snapshot.Content);
        Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(_dir, "missing.txt"), logger));
        Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(_dir, "no-dir", "missing.txt"), logger));
        Assert.Empty(logger.Records);
    }

    [Fact]
    public async Task DoesNotSnapshotFilesOverTheSizeLimitAndLogsWhy()
    {
        var big = Path.Combine(_dir, "big.txt");
        System.IO.File.WriteAllBytes(big, [.. Enumerable.Repeat((byte)0x61, ChangeModel.MaxSnapshotBytes + 1)]);
        var logger = new MemoryLogger();
        Assert.Null(await ChangeModel.ReadSnapshotAsync(big, logger));
        Assert.Matches(@"big\.txt.*exceeds", logger.Text("debug"));
    }

    [Fact]
    public async Task DoesNotSnapshotDirectoriesAndLogsWhy()
    {
        var logger = new MemoryLogger();
        Assert.Null(await ChangeModel.ReadSnapshotAsync(_dir, logger));
        Assert.Contains("not a regular file", logger.Text("debug"));
    }
}
