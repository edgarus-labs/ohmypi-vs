using Omp.Core.Processes;

namespace Omp.Core.Tests;

public class CommandLineMoreTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("dir\\", "dir\\")]
    [InlineData("my dir\\", "\"my dir\\\\\"")]
    [InlineData("a\\\\\"b", "\"a\\\\\\\\\\\"b\"")]
    [InlineData("a b\\c", "\"a b\\c\"")]
    public void QuotingFollowsTheMicrosoftRules(string arg, string expected) => Assert.Equal(expected, CommandLine.Quote(arg));

    [Fact]
    public void ABatchLauncherRunsThroughCmdWithAFallbackWhenComSpecIsMissing()
    {
        var (application, line) = CommandLine.Build("C:\\tools\\omp.cmd", new[] { "--mode", "rpc ui" }, null);
        Assert.EndsWith("cmd.exe", application);
        Assert.Contains("/d /s /c", line);
        var (given, _) = CommandLine.Build("C:\\tools\\omp.bat", new string[0], "C:\\custom\\cmd.exe");
        Assert.Equal("C:\\custom\\cmd.exe", given);
        var (exe, direct) = CommandLine.Build("C:\\bin\\omp.exe", new[] { "a b" }, "ignored");
        Assert.Equal("C:\\bin\\omp.exe", exe);
        Assert.Equal("C:\\bin\\omp.exe \"a b\"", direct);
    }

    [Fact]
    public void TheEnvironmentBlockAppliesOverridesAndRemovals()
    {
        Environment.SetEnvironmentVariable("OMP_TEST_KEEP", "1");
        Environment.SetEnvironmentVariable("OMP_TEST_DROP", "1");
        try
        {
            var block = CommandLine.EnvironmentBlock(new Dictionary<string, string?> { ["OMP_TEST_NEW"] = "2", ["OMP_TEST_DROP"] = null });
            Assert.Contains("OMP_TEST_KEEP=1\0", block);
            Assert.Contains("OMP_TEST_NEW=2\0", block);
            Assert.DoesNotContain("OMP_TEST_DROP", block);
            Assert.EndsWith("\0\0", block);
            Assert.Contains("OMP_TEST_KEEP=1\0", CommandLine.EnvironmentBlock(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OMP_TEST_KEEP", null);
            Environment.SetEnvironmentVariable("OMP_TEST_DROP", null);
        }
    }
}
