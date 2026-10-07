namespace Omp.Core.Tests;

public class ExecutableResolverGapTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    [Fact]
    public void TheHomeVariableStandsInWhenTheUserProfileIsNotSet()
    {
        var found = ExecutableResolver.Locate(null, Env(("HOME", "C:\\home\\u")), path => path == "C:\\home\\u\\.local\\bin\\omp.exe");
        Assert.Equal("C:\\home\\u\\.local\\bin\\omp.exe", found);
    }

    [Fact]
    public void ADirectoryOnThePathThatCannotFormAFileNameIsSkipped()
    {
        var env = Env(("USERPROFILE", "C:\\Users\\u"), ("Path", "C:\\bad<dir;C:\\good"));
        Assert.Equal("C:\\good\\omp.exe", ExecutableResolver.Locate(null, env, path => path == "C:\\good\\omp.exe"));
    }

    [Fact]
    public void WithoutLocalAppDataTheSearchStillListsTheOtherLocations()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate(null, Env(("USERPROFILE", "C:\\Users\\u")), _ => false));
        Assert.Contains("C:\\Users\\u\\.bun\\bin", error.Message);
        Assert.DoesNotContain("AppData", error.Message);
    }
}
