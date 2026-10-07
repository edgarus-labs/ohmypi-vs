using OhMyPi.VisualStudio.Logic;
using System.IO;

namespace OhMyPi.VisualStudio.Tests;

public sealed class LocalPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative.cs")]
    [InlineData("RDT:{guid}|<name>")]
    [InlineData("C:\\a|b.cs")]
    [InlineData("C:\\a\"b.cs")]
    [InlineData("C:\\a\u0001b.cs")]
    public void MonikersThatAreNoExistingPathsAreRejected(string? moniker) => Assert.False(LocalPaths.Exists(moniker, allowDirectories: true));

    [Fact]
    public void FilesAlwaysAndDirectoriesOnRequestAreAccepted()
    {
        var file = typeof(LocalPathsTests).Assembly.Location;
        var directory = Path.GetDirectoryName(file)!;
        Assert.True(LocalPaths.Exists(file, allowDirectories: false));
        Assert.True(LocalPaths.Exists(directory, allowDirectories: true));
        Assert.False(LocalPaths.Exists(directory, allowDirectories: false));
    }
}
