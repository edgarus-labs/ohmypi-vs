using OhMyPi.VisualStudio.Logic;
using System;

namespace OhMyPi.VisualStudio.Tests;

public sealed class ChatPaletteTests
{
    private static readonly Argb Dark = new Argb(0xFF, 0x1E, 0x1E, 0x1E);
    private static readonly Argb Light = new Argb(0xFF, 0xF5, 0xF5, 0xF5);

    private static readonly Argb LightText = new Argb(0xFF, 0xF1, 0xF1, 0xF1);
    private static readonly Argb DarkText = new Argb(0xFF, 0x1E, 0x1E, 0x1E);

    [Fact]
    public void CodeSurfaceLeansTowardWhiteOnDarkAndTowardBlackOnLightBackgrounds()
    {
        var onDark = ChatPalette.CodeSurface(Dark, LightText, false).Surface;
        var onLight = ChatPalette.CodeSurface(Light, DarkText, false).Surface;
        Assert.True(onDark.R > Dark.R && onDark.G > Dark.G && onDark.B > Dark.B);
        Assert.True(onLight.R < Light.R && onLight.G < Light.G && onLight.B < Light.B);
    }

    [Theory]
    [InlineData(0x00, 0xFF)]
    [InlineData(0xFF, 0x00)]
    public void In_high_contrast_code_boxes_keep_the_theme_background_and_take_the_text_color_as_border(byte background, byte text)
    {
        var window = new Argb(0xFF, background, background, background);
        var foreground = new Argb(0xFF, text, text, text);
        var (surface, border) = ChatPalette.CodeSurface(window, foreground, true);
        Assert.Equal(window, surface);
        Assert.Equal(foreground, border);
    }

    [Fact]
    public void CodeSurfaceBorderIsFartherFromTheBackgroundThanTheSurfaceAndBothAreOpaque()
    {
        foreach (var background in new[] { Dark, Light, new Argb(0x80, 0x10, 0x40, 0xC0) })
        {
            var (surface, border) = ChatPalette.CodeSurface(background, LightText, false);
            Assert.Equal(0xFF, surface.A);
            Assert.Equal(0xFF, border.A);
            Assert.True(Math.Abs(border.R - background.R) > Math.Abs(surface.R - background.R));
            Assert.True(Math.Abs(border.B - background.B) > Math.Abs(surface.B - background.B));
        }
    }

    [Fact]
    public void DiffLinesAreTranslucentAndTheirChangedWordsStronger()
    {
        foreach (var dark in new[] { true, false })
        {
            Assert.True(ChatPalette.AddedLine(dark).A < 0xFF);
            Assert.True(ChatPalette.RemovedLine(dark).A < 0xFF);
            Assert.True(ChatPalette.AddedWord(dark).A > ChatPalette.AddedLine(dark).A);
            Assert.True(ChatPalette.RemovedWord(dark).A > ChatPalette.RemovedLine(dark).A);
            Assert.NotEqual(ChatPalette.AddedLine(dark), ChatPalette.RemovedLine(dark));
        }
    }

    [Theory]
    [InlineData(30, 30, 30, true)]
    [InlineData(255, 255, 255, false)]
    [InlineData(0, 0, 255, true)]
    [InlineData(0, 255, 0, false)]
    public void ThemeDarknessFollowsTheLuminanceOfTheBackground(byte r, byte g, byte b, bool dark)
    {
        Assert.Equal(dark, ChatPalette.IsDark(r, g, b));
        Assert.Equal(dark, ChatPalette.IsDark(new Argb(0xFF, r, g, b)));
    }
}
