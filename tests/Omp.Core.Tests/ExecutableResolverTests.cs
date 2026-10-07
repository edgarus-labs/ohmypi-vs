namespace Omp.Core.Tests;

public class ExecutableResolverTests
{
    private static Func<string, bool> FsWith(params string[] files)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        return set.Contains;
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    private static readonly Func<string, string?> WinEnv = Env(("USERPROFILE", "C:\\Users\\u"), ("LOCALAPPDATA", "C:\\Users\\u\\AppData\\Local"), ("Path", "C:\\bin;C:\\tools"));

    [Fact]
    public void UsesAConfiguredPathWithTildeAndEnvironmentExpansionWhenItExists()
    {
        Assert.Equal("C:\\Users\\u\\tools\\omp.exe", ExecutableResolver.Locate("~\\tools\\omp.exe", WinEnv, FsWith("C:\\Users\\u\\tools\\omp.exe")));
        Assert.Equal("C:\\Users\\u\\AppData\\Local\\omp\\omp.exe", ExecutableResolver.Locate("  %LOCALAPPDATA%\\omp\\omp.exe ", WinEnv, FsWith("C:\\Users\\u\\AppData\\Local\\omp\\omp.exe")));
    }

    [Fact]
    public void FailsClearlyWhenTheConfiguredPathDoesNotExist()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("C:\\nope\\omp.exe", WinEnv, FsWith("C:\\bin\\omp.exe")));
        Assert.Contains("C:\\nope\\omp.exe", error.Message);
        Assert.Contains("configured", error.Message);
    }

    [Fact]
    public void RejectsARelativeConfiguredPath()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("tools\\omp.cmd", WinEnv, FsWith("tools\\omp.cmd")));
        Assert.Contains("absolute", error.Message);
        Assert.Contains("tools\\omp.cmd", error.Message);
        Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("\\tools\\omp.cmd", WinEnv, FsWith("\\tools\\omp.cmd")));
    }

    [Fact]
    public void RejectsAConfiguredPathThatWindowsCannotLaunch()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("C:\\tools\\omp", WinEnv, FsWith("C:\\tools\\omp")));
        Assert.Contains(".exe", error.Message);
        Assert.Contains(".cmd", error.Message);
        Assert.Contains("C:\\tools\\omp", error.Message);
    }

    [Fact]
    public void SearchesPathInOrderLookingForOmpExeAndOmpCmd()
    {
        Assert.Equal("C:\\tools\\omp.cmd", ExecutableResolver.Locate("", WinEnv, FsWith("C:\\tools\\omp.cmd")));
        Assert.Equal("C:\\bin\\omp.exe", ExecutableResolver.Locate(null, WinEnv, FsWith("C:\\bin\\omp.exe", "C:\\tools\\omp.exe")));
    }

    [Fact]
    public void SkipsAnExtensionlessOmpForALauncherWindowsCanRun() => Assert.Equal("C:\\tools\\omp.cmd", ExecutableResolver.Locate("", WinEnv, FsWith("C:\\bin\\omp", "C:\\tools\\omp.cmd")));

    [Fact]
    public void FallsBackToLocalBinBunBinAndTheOmpInstallDirectory()
    {
        Assert.Equal("C:\\Users\\u\\.local\\bin\\omp.exe", ExecutableResolver.Locate("", WinEnv, FsWith("C:\\Users\\u\\.local\\bin\\omp.exe")));
        Assert.Equal("C:\\Users\\u\\.bun\\bin\\omp.exe", ExecutableResolver.Locate("", WinEnv, FsWith("C:\\Users\\u\\.bun\\bin\\omp.exe")));
        Assert.Equal("C:\\Users\\u\\AppData\\Local\\omp\\omp.exe", ExecutableResolver.Locate("", WinEnv, FsWith("C:\\Users\\u\\AppData\\Local\\omp\\omp.exe")));
    }

    [Fact]
    public void ListsEveryLocationTriedWhenNothingIsFound()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("", WinEnv, FsWith()));
        foreach (var path in new[] { "C:\\bin\\omp.exe", "C:\\tools\\omp.cmd", "C:\\Users\\u\\.local\\bin\\omp.exe", "C:\\Users\\u\\.bun\\bin\\omp.cmd", "C:\\Users\\u\\AppData\\Local\\omp\\omp.exe" })
        {
            Assert.Contains(path, error.Message);
        }
    }

    [Fact]
    public void SkipsRelativePathEntries()
    {
        var env = Env(("USERPROFILE", "C:\\Users\\u"), ("Path", ".;bin;C:\\tools"));
        Assert.Equal("C:\\tools\\omp.exe", ExecutableResolver.Locate("", env, FsWith(".\\omp.exe", "bin\\omp.exe", "C:\\tools\\omp.exe")));
        var error = Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Locate("", env, FsWith()));
        Assert.DoesNotContain("  bin\\omp", error.Message);
        Assert.DoesNotContain("  .\\omp", error.Message);
    }
}
