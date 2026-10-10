using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class WorkingDirectoryTests
{
    /// <summary>
    /// The profile.
    /// </summary>
    private const string Profile = @"C:\Users\me";

    [Fact]
    public void PrefersTheSolutionDirectory() => Assert.Equal(@"D:\work\repo", WorkingDirectory.Resolve(@"D:\work\repo\", null, Profile));

    [Fact]
    public void UsesTheOpenFolderWithoutSolution()
    {
        Assert.Equal(@"D:\work\folder", WorkingDirectory.Resolve(null, @"D:\work\folder", Profile));
        Assert.Equal(@"D:\work\folder", WorkingDirectory.Resolve("  ", @"D:\work\folder\", Profile));
    }

    [Fact]
    public void FallsBackToTheUserProfile()
    {
        Assert.Equal(Profile, WorkingDirectory.Resolve(null, null, Profile));
        Assert.Equal(Profile, WorkingDirectory.Resolve("", "", Profile));
    }

    [Fact]
    public void KeepsDriveRoots() => Assert.Equal(@"D:\", WorkingDirectory.Resolve(@"D:\", null, Profile));

    [Fact]
    public void ComparesDirectoriesIgnoringCaseAndTrailingSeparators()
    {
        Assert.True(WorkingDirectory.Same(@"D:\Work\Repo\", @"d:\work\repo"));
        Assert.False(WorkingDirectory.Same(@"D:\work\repo", @"D:\work\repo2"));
    }
}
