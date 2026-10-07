namespace Omp.Core.Tests;

public class PathNormalizationTests
{
    [Fact]
    public void AnInvalidPathHasNoNormalFormAndATrailingSeparatorIsDropped()
    {
        Assert.Null(Omp.Core.Changes.ChangePaths.Normalize("bad\0name"));
        Assert.Equal("C:\\repo\\src", Omp.Core.Changes.ChangePaths.Normalize("C:\\repo\\src\\"));
        Assert.Equal("C:\\", Omp.Core.Changes.ChangePaths.Normalize("C:\\"));
    }
}
