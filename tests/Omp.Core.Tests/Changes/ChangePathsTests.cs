using Newtonsoft.Json.Linq;
using Omp.Core.Changes;

namespace Omp.Core.Tests.Changes;

public class ChangePathsTests
{
    private static readonly string Cwd = Path.GetFullPath("D:\\work\\repo");
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void ResolvesRelativePathsAgainstCwdAndKeepsAbsoluteOnes()
    {
        Assert.Equal(Path.Combine(Cwd, "src", "a.ts"), ChangePaths.ResolveToolPath("src/a.ts", Cwd));
        Assert.Equal("E:\\abs\\b.ts", ChangePaths.ResolveToolPath("E:\\abs\\b.ts", Cwd));
        Assert.Equal("D:\\work\\x.ts", ChangePaths.ResolveToolPath("..\\x.ts", Cwd));
    }

    [Fact]
    public void ExpandsALeadingTildeWithEitherSeparatorToTheHomeDirectory()
    {
        Assert.Equal(Home, ChangePaths.ResolveToolPath("~", Cwd));
        Assert.Equal(Path.Combine(Home, "notes.md"), ChangePaths.ResolveToolPath("~/notes.md", Cwd));
        Assert.Equal(Path.Combine(Home, "notes.md"), ChangePaths.ResolveToolPath("~\\notes.md", Cwd));
    }

    [Fact]
    public void IgnoresUrlsAndInternalSchemes()
    {
        Assert.Null(ChangePaths.ResolveToolPath("xd://ast_grep", Cwd));
        Assert.Null(ChangePaths.ResolveToolPath("https://example.com/a", Cwd));
        Assert.Null(ChangePaths.ResolveToolPath("skill://rules", Cwd));
        Assert.Null(ChangePaths.ResolveToolPath("", Cwd));
        Assert.Null(ChangePaths.ResolveToolPath("   ", Cwd));
    }

    [Fact]
    public void AcceptsARootAndPathsBelowItCaseInsensitively()
    {
        Assert.True(ChangePaths.IsWithinRoots("D:\\work\\repo", new[] { "D:\\work\\repo" }));
        Assert.True(ChangePaths.IsWithinRoots("d:\\Work\\Repo\\src\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.True(ChangePaths.IsWithinRoots("D:\\work\\repo\\..data\\a", new[] { "D:\\work\\repo" }));
        Assert.True(ChangePaths.IsWithinRoots("E:\\other\\b.ts", new[] { "D:\\work\\repo", "E:\\other" }));
        Assert.True(ChangePaths.IsWithinRoots("D:\\work\\repo\\a.ts", new[] { "D:\\work\\repo\\" }));
        Assert.True(ChangePaths.IsWithinRoots("D:\\a.ts", new[] { "D:\\" }));
    }

    [Fact]
    public void RejectsPathsOutsideEveryRoot()
    {
        Assert.False(ChangePaths.IsWithinRoots("D:\\work\\repo2\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.False(ChangePaths.IsWithinRoots("D:\\work\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.False(ChangePaths.IsWithinRoots("E:\\work\\repo\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.False(ChangePaths.IsWithinRoots("D:\\work\\a", Array.Empty<string>()));
    }

    [Fact]
    public void RejectsUncPathsOutsideTheRootsAndWindowsDevicePaths()
    {
        Assert.False(ChangePaths.IsWithinRoots("\\\\attacker\\share\\x", new[] { "D:\\work\\repo" }));
        Assert.False(ChangePaths.IsWithinRoots("\\\\?\\D:\\work\\repo\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.False(ChangePaths.IsWithinRoots("\\\\.\\D:\\work\\repo\\a.ts", new[] { "D:\\work\\repo" }));
        Assert.True(ChangePaths.IsWithinRoots("\\\\server\\share\\repo\\a.ts", new[] { "\\\\server\\share\\repo" }));
    }

    [Fact]
    public void CollectsPathFilePathsOldPathNewPathAndEditsPath()
    {
        Assert.Equal(new[] { "a.ts" }, ChangePaths.ToolArgPaths(new JObject { ["path"] = "a.ts" }));
        Assert.Equal(new[] { "b.ts" }, ChangePaths.ToolArgPaths(new JObject { ["file"] = "b.ts" }));
        Assert.Equal(new[] { "c.ts", "d.ts" }, ChangePaths.ToolArgPaths(new JObject { ["paths"] = new JArray("c.ts", 3, "d.ts") }));
        Assert.Equal(new[] { "o.ts", "n.ts" }, ChangePaths.ToolArgPaths(new JObject { ["oldPath"] = "o.ts", ["newPath"] = "n.ts" }));
        Assert.Equal(new[] { "e.ts" }, ChangePaths.ToolArgPaths(new JObject { ["edits"] = new JArray(new JObject { ["path"] = "e.ts" }, new JObject { ["path"] = "e.ts" }, new JObject()) }));
    }

    [Fact]
    public void ParsesApplyPatchStyleHeadersFromAnInputString()
    {
        const string input = "*** Begin Patch\n*** Add File: hello.txt\n+Hello\n*** Update File: src/app.py\n*** Move to: src/main.py\n@@\n*** Delete File: old.txt\n*** End Patch\n";
        Assert.Equal(new[] { "hello.txt", "src/app.py", "src/main.py", "old.txt" }, ChangePaths.ToolArgPaths(new JObject { ["input"] = input }));
    }

    [Fact]
    public void ToolArgPathsReturnsNothingForNonObjects()
    {
        Assert.Empty(ChangePaths.ToolArgPaths(null));
        Assert.Empty(ChangePaths.ToolArgPaths(new JValue("a.ts")));
        Assert.Empty(ChangePaths.ToolArgPaths(new JObject { ["path"] = 5 }));
    }

    private static string Files(JToken? details) =>
        string.Join("|", ChangePaths.ToolResultFiles(details).Select(f => f.OldText == null ? f.Path : $"{f.Path}<{f.OldText}"));

    [Fact]
    public void ReadsDetailsPathResolvedPathAndOldText()
    {
        Assert.Equal("/x/a.ts<old", Files(new JObject { ["path"] = "/x/a.ts", ["oldText"] = "old" }));
        Assert.Equal("/x/w.ts", Files(new JObject { ["resolvedPath"] = "/x/w.ts" }));
    }

    [Fact]
    public void ReadsPerFileResultsEntriesIncludingMoveSources()
    {
        var details = new JObject
        {
            ["perFileResults"] = new JArray(
                new JObject { ["path"] = "/x/a.ts", ["oldText"] = "a0" },
                new JObject { ["path"] = "/x/new.ts", ["sourcePath"] = "/x/old.ts" }),
        };
        Assert.Equal("/x/a.ts<a0|/x/new.ts|/x/old.ts", Files(details));
    }

    [Fact]
    public void DedupesAndToleratesJunk()
    {
        Assert.Equal("/x/a.ts", Files(new JObject { ["path"] = "/x/a.ts", ["resolvedPath"] = "/x/a.ts" }));
        Assert.Equal("", Files(null));
        Assert.Equal("", Files(new JValue("str")));
        Assert.Equal("", Files(new JObject { ["perFileResults"] = "nope" }));
    }

    [Fact]
    public void CountsAddedAndRemovedLines()
    {
        Assert.Equal((2, 1), ChangePaths.LineDelta("a\nb\nc\n", "a\nB\nc\nd\n"));
        Assert.Equal((2, 0), ChangePaths.LineDelta(null, "x\ny"));
        Assert.Equal((0, 2), ChangePaths.LineDelta("x\ny\n", null));
        Assert.Equal((0, 0), ChangePaths.LineDelta("same\n", "same\r\n"));
    }
}
