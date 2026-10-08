using OhMyPi.VisualStudio.UI.Model;
using OhMyPi.VisualStudio.UI.Views;
using System;
using System.Linq;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The colors code tokens take in tool rows: calm shades only, never the saturated status colors, which strain the eyes on a dark theme.</summary>
public sealed class CodeColorsTests
{
    [Fact]
    public void No_token_kind_is_colored_with_a_saturated_status_color()
    {
        var loud = new[] { ThemeKeys.Warning, ThemeKeys.Success, ThemeKeys.Error, ThemeKeys.Link };
        foreach (var kind in Enum.GetValues(typeof(CodeTokenKind)).Cast<CodeTokenKind>())
        {
            var brush = Ui.TokenBrush(kind);
            Assert.DoesNotContain(brush, loud);
        }
    }

    [Fact]
    public void Comments_recede_strings_and_numbers_rise_and_keys_match_keywords()
    {
        Assert.Null(Ui.TokenBrush(CodeTokenKind.Text));
        Assert.Equal(ThemeKeys.Subtle, Ui.TokenBrush(CodeTokenKind.Comment));
        Assert.Equal(ThemeKeys.Foreground, Ui.TokenBrush(CodeTokenKind.String));
        Assert.Equal(ThemeKeys.Foreground, Ui.TokenBrush(CodeTokenKind.Number));
        Assert.Equal(Ui.TokenBrush(CodeTokenKind.Keyword), Ui.TokenBrush(CodeTokenKind.Key));
    }
}
