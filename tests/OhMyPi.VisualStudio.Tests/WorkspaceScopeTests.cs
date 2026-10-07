using OhMyPi.VisualStudio.Logic;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OhMyPi.VisualStudio.Tests;

public sealed class WorkspaceScopeTests
{
    private readonly WorkspaceScope _scope = new(@"D:\work\repo", new[] { @"D:\work\repo", @"D:\elsewhere" });
    private readonly List<string> _asked = new();

    private Task<bool> Confirm(string path, bool answer)
    {
        _asked.Add(path);

        return Task.FromResult(answer);
    }

    [Fact]
    public void ResolvesToolPathsAgainstTheWorkingDirectory()
    {
        Assert.Equal(@"D:\work\repo\src\a.cs", _scope.Resolve(" src/a.cs "));
        Assert.Equal(@"C:\x\b.cs", _scope.Resolve(@"C:\x\b.cs"));
    }

    [Fact]
    public void ExpandsHomeInToolPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(System.IO.Path.Combine(home, "notes.md"), _scope.Resolve("~/notes.md"));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("  ")]
    [InlineData("a|b.cs")]
    [InlineData("a\"b.cs")]
    public void RejectsWhatNamesNoLocalFile(string raw)
    {
        var error = Assert.Throws<InvalidOperationException>(() => _scope.Resolve(raw));
        Assert.Equal($"Not a local file: {raw}", error.Message);
    }

    [Theory]
    [InlineData(@"D:\work\repo\src\a.cs", true)]
    [InlineData(@"D:\work\repo", true)]
    [InlineData(@"d:\work\REPO\a.cs", true)]
    [InlineData(@"D:\elsewhere\a.cs", true)]
    [InlineData(@"D:\work\repository\a.cs", false)]
    [InlineData(@"D:\work\a.cs", false)]
    [InlineData(@"\\?\D:\work\repo\a.cs", false)]
    [InlineData(@"\\.\D:\work\repo\a.cs", false)]
    [InlineData(@"\\host\share\a.cs", false)]
    public void ContainsOnlyPathsBelowItsRoots(string path, bool expected) => Assert.Equal(expected, _scope.Contains(path));

    [Fact]
    public async Task OpensPathsInsideTheWorkspaceWithoutAsking()
    {
        Assert.True(await _scope.MayOpenAsync(@"D:\work\repo\a.cs", path => Confirm(path, false)));
        Assert.Empty(_asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsksBeforeOpeningPathsOutsideTheWorkspace(bool answer)
    {
        Assert.Equal(answer, await _scope.MayOpenAsync(@"\\host\share\secret.txt", path => Confirm(path, answer)));
        Assert.Equal(new[] { @"\\host\share\secret.txt" }, _asked);
    }
}
