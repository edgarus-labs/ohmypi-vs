using OhMyPi.VisualStudio.UI.Model;
using System.Linq;
using System.Text.RegularExpressions;

namespace OhMyPi.VisualStudio.UI.Tests;

public sealed class CodeSegmentsTests
{
    private static string Dump(CodeLine line) =>
        line.Number + "|" + string.Join("", line.Segments.Select(s => s.Marked ? "[" + s.Text + "]" : s.Text));

    [Fact]
    public void A_listing_gets_its_gutter_and_search_matches_are_marked_across_tokens()
    {
        var lines = CodeSegments.Build("var g = app.MapGroup(\"/r\");\n\ng.MapGet(\"/\")", new[] { "*15", "16", "*17" }, "cs", new Regex("Map(Get|Group)\\("));
        Assert.Equal(new[] { "*15|var g = app.[MapGroup(]\"/r\");", " 16|", "*17|g.[MapGet(]\"/\")" }, lines.Select(Dump));
        Assert.Equal(new[] { " 1|a", "  |", "36|b" }, CodeSegments.Build("a\n{ … }\nb", new[] { "1", "26-35", "36" }, null, null).Select(Dump));
        Assert.Equal(new[] { "var", " g = app.", "MapGroup(", "\"/r\"", ");" }, lines[0].Segments.Select(s => s.Text));
        Assert.Equal(CodeTokenKind.String, lines[0].Segments[3].Kind);

        var plain = CodeSegments.Build("a\nb", null, null, null);
        Assert.Equal(new[] { "|a", "|b" }, plain.Select(Dump));
    }
}
