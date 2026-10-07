using OhMyPi.VisualStudio.Logic.Automation;
using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.Tests;

public sealed class LineEditsTests
{
    private static string Apply(string document, int startLine, int endLine, string text, string lineBreak = "\n")
    {
        var lines = LineEdits.SplitLines(document);
        var edit = LineEdits.Plan(lines.Count, line => lines[line].Length, startLine, endLine, text, lineBreak);
        var start = Offset(lines, edit.StartLine, edit.StartIndex, lineBreak);
        var end = Offset(lines, edit.EndLine, edit.EndIndex, lineBreak);

        return document.Substring(0, start) + edit.Text + document.Substring(end);
    }

    private static int Offset(IReadOnlyList<string> lines, int line, int index, string lineBreak)
    {
        var offset = 0;
        for (var i = 0; i < line; i++)
        {
            offset += lines[i].Length + lineBreak.Length;
        }

        return offset + index;
    }

    [Theory]
    [InlineData("a\nb\nc", 2, 2, "B", "a\nB\nc")]
    [InlineData("a\nb\nc", 2, 3, "X\nY", "a\nX\nY")]
    [InlineData("a\nb\nc", 1, 1, "A\n", "A\nb\nc")]
    [InlineData("a\nb\nc", 1, 3, "z", "z")]
    [InlineData("a\nb\nc\n", 2, 2, "B", "a\nB\nc\n")]
    public void ReplacesWholeLines(string document, int start, int end, string text, string expected) =>
        Assert.Equal(expected, Apply(document, start, end, text));

    [Theory]
    [InlineData("a\nb\nc", 2, 2, "a\nc")]
    [InlineData("a\nb\nc", 1, 1, "b\nc")]
    [InlineData("a\nb\nc", 3, 3, "a\nb")]
    [InlineData("a\nb\nc", 1, 3, "")]
    public void EmptyTextDeletesTheLines(string document, int start, int end, string expected) =>
        Assert.Equal(expected, Apply(document, start, end, ""));

    [Theory]
    [InlineData("a\nc", 2, 1, "b", "a\nb\nc")]
    [InlineData("a\nc", 1, 0, "z", "z\na\nc")]
    [InlineData("a\nb", 3, 2, "c", "a\nb\nc")]
    public void InsertsBeforeTheStartLine(string document, int start, int end, string text, string expected) =>
        Assert.Equal(expected, Apply(document, start, end, text));

    [Fact]
    public void JoinsMultiLineTextWithTheBufferLineBreak() =>
        Assert.Equal("a\r\nX\r\nY\r\nc", Apply("a\r\nb\r\nc", 2, 2, "X\nY", "\r\n"));

    [Theory]
    [InlineData(0, 0, "startLine")]
    [InlineData(5, 5, "past the end")]
    [InlineData(2, 5, "endLine")]
    [InlineData(3, 1, "endLine")]
    public void RejectsRangesThatDoNotFit(int start, int end, string messagePart)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LineEdits.Plan(3, _ => 1, start, end, "x", "\n"));
        Assert.Contains(messagePart, error.Message);
    }

    [Theory]
    [InlineData("a\r\nb\nc\rd", new[] { "a", "b", "c", "d" })]
    [InlineData("a\n", new[] { "a", "" })]
    [InlineData("", new[] { "" })]
    public void SplitsOnEveryLineBreakKind(string text, string[] expected) =>
        Assert.Equal(expected, LineEdits.SplitLines(text));

    [Theory]
    [InlineData("a\r\n", "\r\n")]
    [InlineData("a\n", "\n")]
    [InlineData("a\r", "\r")]
    [InlineData("a", "\r\n")]
    [InlineData(null, "\r\n")]
    public void DetectsTheLineBreakOfTheFirstLine(string? firstLine, string expected) =>
        Assert.Equal(expected, LineEdits.DetectLineBreak(firstLine));

    [Theory]
    [InlineData(null, null, 1, 3)]
    [InlineData(2, null, 2, 3)]
    [InlineData(2, 99, 2, 3)]
    [InlineData(1, 2, 1, 2)]
    public void ResolvesTheReadRange(int? start, int? end, int expectedFirst, int expectedLast)
    {
        LineEdits.ResolveRange(3, start, end, out var first, out var last);
        Assert.Equal((expectedFirst, expectedLast), (first, last));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(4, null)]
    [InlineData(3, 2)]
    public void RejectsAnEmptyOrOutOfRangeReadRange(int start, int? end) =>
        Assert.Throws<InvalidOperationException>(() => LineEdits.ResolveRange(3, start, end, out _, out _));
}
