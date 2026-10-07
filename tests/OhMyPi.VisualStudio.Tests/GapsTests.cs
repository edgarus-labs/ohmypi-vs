using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class DiffSourceTests
{
    [Fact]
    public void TwoSourcesAreEqualWhenTheyHoldTheSameContentAndState()
    {
        var a = new DiffSource(false, "before");
        Assert.True(a.Equals(new DiffSource(false, "before")));
        Assert.True(a.Equals((object)new DiffSource(false, "before")));
        Assert.False(a.Equals(new DiffSource(true, "before")));
        Assert.False(a.Equals(new DiffSource(false, "other")));
        Assert.False(a.Equals("before"));
        Assert.Equal(a.GetHashCode(), new DiffSource(false, "before").GetHashCode());
        Assert.Equal("Deleted=False, Before=before", a.ToString());
    }
}
