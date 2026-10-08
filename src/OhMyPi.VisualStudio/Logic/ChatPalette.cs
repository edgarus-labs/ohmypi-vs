using System;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>
/// The colors of the chat transcript that depend on the theme: code surfaces derived from the window background
/// so they stand out on any theme, and GitHub-like soft diff tints, slightly stronger on light themes.
/// </summary>
internal static class ChatPalette
{
    /// <summary>
    /// The opaque background of cards, inline code, table headers, the tool-name pill and the send button, and the
    /// border of those and of tool output: the window background nudged toward white on dark themes and toward black
    /// on light ones, the border farther than the surface. In high contrast the box keeps the window background and
    /// takes the text color as its border, so it keeps the contrast the theme guarantees.
    /// </summary>
    public static (Argb Surface, Argb Border) CodeSurface(Argb background, Argb text, bool highContrast) =>
        highContrast ? (background, text) : (Shift(background, 0.10, 0.06), Shift(background, 0.22, 0.16));

    /// <summary>
    /// The opaque background of a tool's result. When every channel of the window background has at least
    /// <see cref="OutputDistance"/> of room away from the text, it moves 8% of that room away from the text, and at
    /// least <see cref="OutputDistance"/>; otherwise (pure white, pure black and close to them) it moves
    /// <see cref="OutputDistance"/> toward the text, short of the code surface. Either way a result never shares a
    /// shade with the window or the code surface. High contrast keeps the window background.
    /// </summary>
    public static Argb OutputSurface(Argb background, bool highContrast)
    {
        if (highContrast)
        {
            return background;
        }

        var dark = IsDark(background);
        var room = dark ? Math.Min(background.R, Math.Min(background.G, background.B)) : 255 - Math.Max(background.R, Math.Max(background.G, background.B));
        if (room < OutputDistance)
        {
            return new Argb(0xFF, Step(background.R, dark, OutputDistance), Step(background.G, dark, OutputDistance), Step(background.B, dark, OutputDistance));
        }

        return new Argb(0xFF, Away(background.R, dark), Away(background.G, dark), Away(background.B, dark));

        static byte Away(byte c, bool dark)
        {
            var channelRoom = dark ? c : 255 - c;
            return Step(c, !dark, Math.Max(OutputDistance, (int)Math.Round(channelRoom * 0.08)));
        }
    }

    /// <summary>The smallest per-channel distance of the output surface from the window background.</summary>
    private const int OutputDistance = 8;

    /// <summary>Moves a channel by <paramref name="distance"/> toward white when <paramref name="up"/>, toward black otherwise, clamped to 0–255.</summary>
    private static byte Step(byte c, bool up, int distance) => (byte)Math.Max(0, Math.Min(255, up ? c + distance : c - distance));

    public static Argb AddedLine(bool darkTheme) => darkTheme ? new Argb(0x26, 0x3F, 0xB9, 0x50) : new Argb(0x33, 0x2D, 0xA4, 0x4E);

    public static Argb RemovedLine(bool darkTheme) => darkTheme ? new Argb(0x26, 0xF8, 0x51, 0x49) : new Argb(0x33, 0xCF, 0x22, 0x2E);

    public static Argb AddedWord(bool darkTheme) => darkTheme ? new Argb(0x66, 0x3F, 0xB9, 0x50) : new Argb(0x55, 0x2D, 0xA4, 0x4E);

    public static Argb RemovedWord(bool darkTheme) => darkTheme ? new Argb(0x66, 0xF8, 0x51, 0x49) : new Argb(0x55, 0xCF, 0x22, 0x2E);

    /// <summary>Whether a background color is dark, by its luminance.</summary>
    public static bool IsDark(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;

    public static bool IsDark(Argb background) => IsDark(background.R, background.G, background.B);

    /// <summary>Mixes the window background toward the text (white on dark, black on light) by the fraction for its side.</summary>
    private static Argb Shift(Argb background, double onDark, double onLight)
    {
        var dark = IsDark(background);
        var target = dark ? 255 : 0;
        var fraction = dark ? onDark : onLight;

        return new Argb(0xFF, Mix(background.R, target, fraction), Mix(background.G, target, fraction), Mix(background.B, target, fraction));
    }

    private static byte Mix(byte c, int target, double fraction) => (byte)Math.Round(c + (target - c) * fraction);
}
