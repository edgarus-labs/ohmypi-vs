using OhMyPi.VisualStudio.Logic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("--profile work", new[] { "--profile", "work" })]
    [InlineData("  --a   b  ", new[] { "--a", "b" })]
    [InlineData("--name \"two words\" x", new[] { "--name", "two words", "x" })]
    [InlineData("--arg=\"a b\"c", new[] { "--arg=a bc" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("say \\\"hi\\\"", new[] { "say", "\"hi\"" })]
    [InlineData("C:\\dir\\ \"C:\\with space\\\\\"", new[] { "C:\\dir\\", "C:\\with space\\" })]
    [InlineData("\"unterminated value", new[] { "unterminated value" })]
    public void SplitsLikeWindowsCommandLines(string input, string[] expected) => Assert.Equal(expected, CommandLine.Split(input));

    [Fact]
    public void NullSplitsToNothing() => Assert.Empty(CommandLine.Split(null));
}
