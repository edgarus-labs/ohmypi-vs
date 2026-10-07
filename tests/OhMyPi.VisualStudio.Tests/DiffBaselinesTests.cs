using OhMyPi.VisualStudio.Logic;
using System;
using System.IO;
using System.Linq;

namespace OhMyPi.VisualStudio.Tests;

public sealed class DiffBaselinesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "ohmypi-tests", Guid.NewGuid().ToString("N"));
    private readonly DiffBaselines _baselines;

    public DiffBaselinesTests()
    {
        _baselines = new DiffBaselines(_parent, 4242);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_parent))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_parent, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_parent, recursive: true);
    }

    [Fact]
    public void TheRootIsNamedAfterTheOwningProcess()
    {
        Assert.Equal(_parent, Path.GetDirectoryName(_baselines.Root));
        Assert.StartsWith("4242-", Path.GetFileName(_baselines.Root));
    }

    [Fact]
    public void WritesAReadOnlyCopyNamedLikeTheFile()
    {
        var copy = _baselines.Write(_baselines.Batch, @"D:\work\repo\a.cs", "before\n");
        Assert.Equal("a.cs", Path.GetFileName(copy));
        Assert.StartsWith(_baselines.BatchDirectory(_baselines.Batch) + Path.DirectorySeparatorChar, copy);
        Assert.Equal("before\n", File.ReadAllText(copy));
        Assert.True(File.GetAttributes(copy).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void ReusesTheCopyOfIdenticalContentAndKeepsOthersApart()
    {
        var batch = _baselines.Batch;
        var first = _baselines.Write(batch, @"D:\work\repo\a.cs", "one");
        Assert.Equal(first, _baselines.Write(batch, @"D:\work\repo\a.cs", "one"));
        var other = _baselines.Write(batch, @"D:\work\repo\a.cs", "two");
        Assert.NotEqual(first, other);
        Assert.Equal("one", File.ReadAllText(first));
        Assert.Equal(2, Directory.GetFiles(_baselines.BatchDirectory(batch), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void RotateHandsBackThePreviousBatchAndMovesOn()
    {
        var batch = _baselines.Batch;
        var copy = _baselines.Write(batch, @"D:\work\repo\a.cs", "one");
        var previous = _baselines.Rotate();
        Assert.Equal(_baselines.BatchDirectory(batch), previous);
        Assert.NotEqual(batch, _baselines.Batch);

        DiffBaselines.Delete(previous);
        Assert.False(File.Exists(copy));
        Assert.False(Directory.Exists(previous));
    }

    [Fact]
    public void DeletingAMissingDirectoryIsFine() => DiffBaselines.Delete(Path.Combine(_parent, "missing"));

    [Fact]
    public void SweepDeletesTheRootsOfProcessesThatAreGone()
    {
        var alive = Directory.CreateDirectory(Path.Combine(_parent, "111-aaaa")).FullName;
        var gone = Directory.CreateDirectory(Path.Combine(_parent, "222-bbbb", "1")).Parent!.FullName;
        File.WriteAllText(Path.Combine(gone, "1", "a.cs"), "x");
        File.SetAttributes(Path.Combine(gone, "1", "a.cs"), FileAttributes.ReadOnly);
        var foreign = Directory.CreateDirectory(Path.Combine(_parent, "not-a-root")).FullName;

        var failures = DiffBaselines.SweepStale(_parent, pid => pid == 111);

        Assert.Empty(failures);
        Assert.True(Directory.Exists(alive));
        Assert.False(Directory.Exists(gone));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public void SweepOfAMissingParentFindsNothing() => Assert.Empty(DiffBaselines.SweepStale(Path.Combine(_parent, "missing"), _ => false));

    [Fact]
    public void SweepKeepsTheRootsOfThisProcess()
    {
        _baselines.Write(_baselines.Batch, @"D:\work\repo\a.cs", "one");
        DiffBaselines.SweepStale(_parent, pid => pid == 4242);
        Assert.True(Directory.EnumerateFiles(_baselines.Root, "*", SearchOption.AllDirectories).Any());
    }
}
