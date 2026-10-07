using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class JsonTextTests
{
    [Fact]
    public void Compact_objects_and_arrays_are_indented()
    {
        Assert.Equal("{\n  \"review_threads\": [],\n  \"totalCount\": 0\n}", JsonText.Pretty("{\"review_threads\":[],\"totalCount\":0}"));
        Assert.Equal("[\n  1,\n  {\n    \"a\": true,\n    \"b\": null\n  }\n]", JsonText.Pretty(" [1, {\"a\" : true,\"b\":null}] \n"));
    }

    [Fact]
    public void Strings_and_numbers_are_copied_verbatim()
    {
        const string source = "{\"path\":\"C:\\\\a\\/b\\n\\u00E9\\\"x\",\"n\":1.50e+10,\"neg\":-0,\"dup\":1,\"dup\":2}";
        var pretty = JsonText.Pretty(source)!;
        Assert.Contains("\"path\": \"C:\\\\a\\/b\\n\\u00E9\\\"x\"", pretty);
        Assert.Contains("\"n\": 1.50e+10", pretty);
        Assert.Contains("\"neg\": -0", pretty);
        Assert.Contains("\"dup\": 1,", pretty);
        Assert.Contains("\"dup\": 2", pretty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("9f3e2f3")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    [InlineData("{'a':1}")]
    [InlineData("{a:1}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,2,]")]
    [InlineData("{\"a\":01}")]
    [InlineData("{\"a\":NaN}")]
    [InlineData("{\"a\":\"\\x41\"}")]
    [InlineData("{\"a\":\"\\u12G4\"}")]
    [InlineData("{\"a\":\"tab\there\"}")]
    [InlineData("{\"a\":1} trailing")]
    [InlineData("{\"a\":1}{\"b\":2}")]
    [InlineData("{\"a\":/*c*/1}")]
    [InlineData("{\"a\":1")]
    [InlineData("[\"unterminated]")]
    [InlineData("{\"a\"}")]
    [InlineData("{\"a\":1}\n{\"b\":2}")]
    public void Anything_else_stays_unchanged(string text) => Assert.Null(JsonText.Pretty(text));

    [Fact]
    public void Already_indented_json_and_very_deep_nesting_stay_unchanged()
    {
        Assert.Null(JsonText.Pretty("{\n  \"a\": 1\n}"));
        Assert.Null(JsonText.Pretty(new string('[', 5000) + new string(']', 5000)));
    }
}
