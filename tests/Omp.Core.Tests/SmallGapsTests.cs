using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Omp.Core.Internal;

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
