using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Omp.Core.Changes;
using Omp.Core.Internal;
using Omp.Core.Processes;
using Omp.Core.Session;
using Omp.Core.Tests.Support;

namespace Omp.Core.Tests;

public class JsonHelpersTests
{
    [Fact]
    public void ParseRejectsContentAfterTheValueButAllowsTrailingComments()
    {
        Assert.Equal(1, (int)Json.Parse("1 /* done */")!);
        Assert.Throws<JsonReaderException>(() => Json.Parse("1 2"));
    }

    [Fact]
    public void ReadersReturnNullForTheWrongShapeAndValuesForTheRightOne()
    {
        var obj = JObject.Parse("{\"s\":\"x\",\"n\":3.9,\"b\":true,\"list\":[\"a\",1,\"b\"],\"nested\":{}}");
        Assert.Equal("x", Json.Str(obj, "s"));
        Assert.Null(Json.Str(obj, "n"));
        Assert.Null(Json.Get(new JArray(), "s"));
        Assert.Equal(3L, Json.Long(obj, "n"));
        Assert.Null(Json.Long(obj, "s"));
        Assert.Null(Json.Num(obj, "s"));
        Assert.True(Json.Bool(obj, "b"));
        Assert.Null(Json.Bool(obj, "s"));
        Assert.Equal(new[] { "a", "b" }, Json.Strings(obj["list"]));
        Assert.Empty(Json.Strings(obj["s"]));
        Assert.Same(obj["list"], Json.Array(obj, "list"));
        Assert.Null(Json.Array(obj, "s"));
        Assert.Equal("{\"a\":1}", Json.Serialize(JObject.Parse("{ \"a\": 1 }")));
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("0.0", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"x\"", true)]
    [InlineData("null", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("[]", true)]
    [InlineData("{}", true)]
    public void TruthinessFollowsJavaScript(string json, bool expected) => Assert.Equal(expected, Json.Truthy(JToken.Parse(json)));

    [Fact]
    public void TruthinessOfNothingAndNaNIsFalse()
    {
        Assert.False(Json.Truthy(null));
        Assert.False(Json.Truthy(new JValue(double.NaN)));
        Assert.False(Json.Truthy(JValue.CreateUndefined()));
    }

    [Theory]
    [InlineData("9007199254740991", true)]
    [InlineData("9007199254740992", false)]
    [InlineData("1.5", false)]
    [InlineData("\"1\"", false)]
    public void SafeIntegersAreWholeNumbersWithinTheJavaScriptRange(string json, bool expected) => Assert.Equal(expected, Json.IsSafeInteger(JToken.Parse(json)));

    [Fact]
    public void TextOfJoinsTextBlocksAndIgnoresTheRest()
    {
        var content = JArray.Parse("[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"b\"}]");
        Assert.Equal("a|b", Json.TextOf(content, "|"));
        Assert.Equal("plain", Json.TextOf(new JValue("plain")));
        Assert.Equal("", Json.TextOf(new JObject()));
    }
}

public class ListenerTests
{
    [Fact]
    public void AFailingListenerIsLoggedAndTheOthersStillRun()
    {
        var logger = new MemoryLogger();
        var seen = new List<string>();
        Action<string>? many = v => throw new InvalidOperationException("boom " + v);
        many += seen.Add;
        Listeners.Raise(many, "event", "x", logger);
        Assert.Equal(new[] { "x" }, seen);
        Assert.Contains("OMP event listener failed", logger.Text("error"));

        Action? plain = () => throw new InvalidOperationException("plain boom");
        var ran = 0;
        plain += () => ran++;
        Listeners.Raise(plain, "tick", logger);
        Assert.Equal(1, ran);
        Assert.Contains("OMP tick listener failed", logger.Text("error"));
    }

    [Fact]
    public void RaisingNothingDoesNothing()
    {
        var logger = new MemoryLogger();
        Listeners.Raise((Action<string>?)null, "e", "x", logger);
        Listeners.Raise((Action?)null, "e", logger);
        Assert.Empty(logger.Records);
        Assert.True(Listeners.NowMs() > 0);
    }
}

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

public class ModelIdentityTests
{
    [Fact]
    public void TheVendorFamilyAndRevisionOmpReportsAreKept()
    {
        var view = ModelMapper.ToModelView(JObject.Parse("{\"id\":\"m\",\"provider\":\"p\",\"identity\":{\"class\":\"anthropic\",\"family\":\"opus\",\"revision\":\"5.5.0\"}}"));
        Assert.Equal(("anthropic", "opus", "5.5.0"), (view.VendorClass, view.Family, view.Revision));
        var none = ModelMapper.ToModelView(JObject.Parse("{\"id\":\"m\",\"provider\":\"p\"}"));
        Assert.Equal((null, null, null), (none.VendorClass, none.Family, none.Revision));
    }
}

public class SnapshotTests
{
    [Fact]
    public async Task ADirectoryAFileTooLargeAndAMissingFileAreNotSnapshottedAsText()
    {
        var logger = new MemoryLogger(trace: true);
        var dir = Path.Combine(Path.GetTempPath(), "omp-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(await ChangeModel.ReadSnapshotAsync(dir, logger));
            Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(dir, "gone.txt"), logger));
            Assert.Same(Snapshot.Missing, await ChangeModel.ReadSnapshotAsync(Path.Combine(dir, "nodir", "gone.txt"), logger));
            Assert.Null(await ChangeModel.ReadSnapshotAsync("bad\0name", logger));
            Assert.Contains("Cannot snapshot", logger.Text("warn"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

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
