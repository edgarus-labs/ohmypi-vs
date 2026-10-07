using OhMyPi.VisualStudio.UI.Views;
using System;
using System.Linq;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>How a provider gets its marker color: brand, black-and-white brand, or derived from the theme accent.</summary>
public sealed class ThemeTintTests
{
    private static readonly Color DarkAccent = Color.FromRgb(0x1E, 0x3A, 0x5F);
    private static readonly Color LightAccent = Color.FromRgb(0xC8, 0xE0, 0xF8);

    private static double Lightness(Color color) => (Math.Max(color.R, Math.Max(color.G, color.B)) + Math.Min(color.R, Math.Min(color.G, color.B))) / 2.0 / 255;

    [Theory]
    [InlineData("anthropic", 0xD9, 0x77, 0x57)]
    [InlineData("openai-codex", 0x10, 0xA3, 0x7F)]
    [InlineData("Google-Antigravity", 0x42, 0x85, 0xF4)]
    public void A_known_brand_keeps_its_color_in_any_theme(string provider, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromRgb(r, g, b), ThemeTint.Marker(DarkAccent, provider));
        Assert.Equal(Color.FromRgb(r, g, b), ThemeTint.Marker(LightAccent, provider));
    }

    [Fact]
    public void A_black_and_white_brand_is_a_gray_kept_between_the_readable_lightness_bounds()
    {
        var onDark = ThemeTint.Marker(DarkAccent, "cursor");
        var onLight = ThemeTint.Marker(LightAccent, "cursor");
        Assert.True(onDark.R == onDark.G && onDark.G == onDark.B);
        Assert.Equal(0.45, Lightness(onDark), 2);
        Assert.Equal(0.7, Lightness(onLight), 2);
    }

    [Fact]
    public void Unknown_providers_get_distinct_stable_colors_as_light_as_the_bounded_accent()
    {
        var accent = Color.FromRgb(0x80, 0x40, 0x40);
        var colors = Enumerable.Range(0, 60).Select(i => ThemeTint.Marker(accent, "provider-" + i)).ToList();
        Assert.Equal(colors, [.. Enumerable.Range(0, 60).Select(i => ThemeTint.Marker(accent, "PROVIDER-" + i))]);
        Assert.True(colors.Distinct().Count() >= 50);
        Assert.All(colors, color => Assert.InRange(Lightness(color), 0.44, 0.46));
    }

    [Fact]
    public void A_gray_accent_still_gives_unknown_providers_a_visible_hue()
    {
        var color = ThemeTint.Marker(Color.FromRgb(0x80, 0x80, 0x80), "someone");
        Assert.False(color.R == color.G && color.G == color.B);
    }
}
