using System;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>
/// The colors of the chat transcript that depend on the theme: code surfaces derived from the window background
/// so they stand out on any theme, and GitHub-like soft diff tints, slightly stronger on light themes.
/// </summary>
internal static class ChatPalette
{
    /// <summary>
    /// The opaque background of code, tool output and table headers, and its border: the window background nudged
    /// toward white on dark themes and toward black on light ones, the border farther than the surface. In high
    /// contrast the box keeps the window background and takes the text color as its border, so it keeps the
    /// contrast the theme guarantees.
    /// </summary>
    public static (Argb Surface, Argb Border) CodeSurface(Argb background, Argb text, bool highContrast) =>
        highContrast ? (background, text) : (Shift(background, 0.07, 0.04), Shift(background, 0.16, 0.12));

    public static Argb AddedLine(bool darkTheme) => darkTheme ? new Argb(0x26, 0x3F, 0xB9, 0x50) : new Argb(0x33, 0x2D, 0xA4, 0x4E);

    public static Argb RemovedLine(bool darkTheme) => darkTheme ? new Argb(0x26, 0xF8, 0x51, 0x49) : new Argb(0x33, 0xCF, 0x22, 0x2E);

    public static Argb AddedWord(bool darkTheme) => darkTheme ? new Argb(0x66, 0x3F, 0xB9, 0x50) : new Argb(0x55, 0x2D, 0xA4, 0x4E);

    public static Argb RemovedWord(bool darkTheme) => darkTheme ? new Argb(0x66, 0xF8, 0x51, 0x49) : new Argb(0x55, 0xCF, 0x22, 0x2E);

    /// <summary>Whether a background color is dark, by its luminance.</summary>
    public static bool IsDark(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;

    public static bool IsDark(Argb background) => IsDark(background.R, background.G, background.B);

    private static Argb Shift(Argb background, double towardWhite, double towardBlack)
    {
        var dark = IsDark(background);
        var target = dark ? 255 : 0;
        var fraction = dark ? towardWhite : towardBlack;

        return new Argb(0xFF, Mix(background.R, target, fraction), Mix(background.G, target, fraction), Mix(background.B, target, fraction));
    }

    private static byte Mix(byte c, int target, double fraction) => (byte)Math.Round(c + (target - c) * fraction);
}
