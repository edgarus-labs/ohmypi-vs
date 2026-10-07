using OhMyPi.VisualStudio.Logic;
using System;
using System.IO;

namespace OhMyPi.VisualStudio.Tests;

public sealed class StaleBaselinesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "ohmypi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_parent))
        {
            Directory.Delete(_parent, recursive: true);
        }
    }

    [Fact]
    public void ARootThatCannotBeDeletedIsReportedAndTheSweepContinues()
    {
        var locked = Path.Combine(_parent, "111-a", "1");
        Directory.CreateDirectory(locked);
        var other = Directory.CreateDirectory(Path.Combine(_parent, "222-b")).FullName;
        using (new FileStream(Path.Combine(locked, "a.cs"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var failures = DiffBaselines.SweepStale(_parent, _ => false);
            var failure = Assert.Single(failures);
            Assert.EndsWith("111-a", failure.Root);
            Assert.False(Directory.Exists(other));
        }
    }
}
