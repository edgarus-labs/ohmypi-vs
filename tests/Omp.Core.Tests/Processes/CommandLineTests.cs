using Omp.Core.Processes;

namespace Omp.Core.Tests.Processes;

public class CommandLineTests
{
    [Fact]
    public void QuotesArgumentsForTheMicrosoftRuntimeRules()
    {
        Assert.Equal("plain", CommandLine.Quote("plain"));
        Assert.Equal("\"\"", CommandLine.Quote(""));
        Assert.Equal("\"a b\"", CommandLine.Quote("a b"));
        Assert.Equal("\"say \\\"hi\\\"\"", CommandLine.Quote("say \"hi\""));
        Assert.Equal("\"C:\\dir with space\\\\\"", CommandLine.Quote("C:\\dir with space\\"));
        Assert.Equal("C:\\dir\\file", CommandLine.Quote("C:\\dir\\file"));
    }

    [Fact]
    public void StartsAnExecutableDirectlyWithQuotedArguments()
    {
        var (application, line) = CommandLine.Build("C:\\Program Files\\omp\\omp.exe", new[] { "--mode", "rpc-ui", "a b" }, "C:\\Windows\\system32\\cmd.exe");
        Assert.Equal("C:\\Program Files\\omp\\omp.exe", application);
        Assert.Equal("\"C:\\Program Files\\omp\\omp.exe\" --mode rpc-ui \"a b\"", line);
    }

    [Fact]
    public void RunsBatchLaunchersThroughCmdWithCaretEscaping()
    {
        var (application, line) = CommandLine.Build("C:\\my tools\\omp.cmd", new[] { "--mode", "x&y" }, "C:\\Windows\\system32\\cmd.exe");
        Assert.Equal("C:\\Windows\\system32\\cmd.exe", application);
        Assert.Equal("C:\\Windows\\system32\\cmd.exe /d /s /c \"C:\\my^ tools\\omp.cmd ^^^\"--mode^^^\" ^^^\"x^^^&y^^^\"\"", line);
    }

    [Fact]
    public void BuildsASortedEnvironmentBlockWithOverridesAndRemovals()
    {
        var name = "OMP_CORE_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "inherited");
        try
        {
            var block = CommandLine.EnvironmentBlock(new Dictionary<string, string?> { ["ZZ_OMP_ADDED"] = "1", [name] = null });
            Assert.EndsWith("\0\0", block);
            var entries = block.TrimEnd('\0').Split('\0');
            Assert.Contains("ZZ_OMP_ADDED=1", entries);
            Assert.DoesNotContain(entries, e => e.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(entries.OrderBy(e => e.Split('=')[0], StringComparer.OrdinalIgnoreCase), entries);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
