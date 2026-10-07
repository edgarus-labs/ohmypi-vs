using Omp.Core.Changes;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests;

public class SnapshotTests
{
    [Fact]
    public async Task ADirectoryAFileTooLargeAndAMissingFileAreNotSnapshottedAsText()
    {
        var logger = new MemoryLogger(trace: true);
        var dir = Path.Combine(Path.GetTempPath(), "omp-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(await ChangeModel.ReadSnapshotAsync(dir, logger));
            Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(dir, "gone.txt"), logger));
            Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(dir, "nodir", "gone.txt"), logger));
            Assert.Null(await ChangeModel.ReadSnapshotAsync("bad\0name", logger));
            Assert.Contains("Cannot snapshot", logger.Text("warn"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
