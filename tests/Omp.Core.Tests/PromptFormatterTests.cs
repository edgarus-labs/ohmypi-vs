namespace Omp.Core.Tests;

public class PromptFormatterTests
{
    private static PromptParts Parts(string text, string? editorContext = null, IEnumerable<PastedText>? pasted = null, IEnumerable<string>? files = null) =>
        new()
        {
            Text = text,
            EditorContext = editorContext,
            Pasted = (pasted ?? Array.Empty<PastedText>()).ToArray(),
            Files = (files ?? Array.Empty<string>()).ToArray(),
        };

    [Fact]
    public void PlacesEditorContextFirstThenTextThenAttachedMentions()
    {
        var message = PromptFormatter.Format(Parts("go", "<editor-context>\nfile: a.ts\n</editor-context>", files: new[] { "a.ts" }));
        Assert.Equal("<editor-context>\nfile: a.ts\n</editor-context>\n\ngo\n\nAttached: @a.ts", message);
    }

    [Fact]
    public void RecoversTextEditorContextPastedBlocksAndAttachedFilesFromAComposedPrompt()
    {
        var message = PromptFormatter.Format(Parts(
            "review",
            "<editor-context>\nx\n</editor-context>",
            new[] { new PastedText { Name = "Pasted text 1", Text = "a\n</pasted-text>\nb" } },
            new[] { "src\\a b.ts", "src\\c.ts" }));
        var parts = PromptFormatter.SplitUserMessage(message);
        Assert.Equal("review", parts.Text);
        Assert.Equal("<editor-context>\nx\n</editor-context>", parts.EditorContext);
        var pasted = Assert.Single(parts.Pasted);
        Assert.Equal("Pasted text 1", pasted.Name);
        Assert.Equal("a\n</pasted-text>\nb", pasted.Text);
        Assert.Equal(new[] { "src\\a b.ts", "src\\c.ts" }, parts.Files);
    }

    [Fact]
    public void LeavesOrdinaryMessagesUntouched()
    {
        var parts = PromptFormatter.SplitUserMessage("Attached is a list: @me");
        Assert.Equal("Attached is a list: @me", parts.Text);
        Assert.Null(parts.EditorContext);
        Assert.Empty(parts.Pasted);
        Assert.Empty(parts.Files);
    }

    [Fact]
    public void RoundTripsFileNamesContainingQuotes()
    {
        var message = PromptFormatter.Format(Parts("look", files: new[] { "a\"b.txt", "c.txt" }));
        Assert.Equal("look\n\nAttached: @\"a\\\"b.txt\" @c.txt", message);
        Assert.Equal(new[] { "a\"b.txt", "c.txt" }, PromptFormatter.SplitUserMessage(message).Files);
    }

    [Fact]
    public void KeepsBackslashesOfWindowsPathsInQuotedMentions()
    {
        Assert.Equal(new[] { "C:\\my docs\\a.md" }, PromptFormatter.SplitUserMessage("go\n\nAttached: @\"C:\\my docs\\a.md\"").Files);
        Assert.Equal("go\n\nAttached: @\"C:\\my docs\\a.md\"", PromptFormatter.Format(Parts("go", files: new[] { "C:\\my docs\\a.md" })));
    }

    [Fact]
    public void ReplacesQuotesInPastedNames()
    {
        var message = PromptFormatter.Format(Parts("", pasted: new[] { new PastedText { Name = "say \"hi\"", Text = "body" } }));
        Assert.Equal("<pasted-text name=\"say 'hi'\">\nbody\n</pasted-text>", message);
    }
}
