using System;

namespace OhMyPi.VisualStudio.Logic
{
    /// <summary>A color as alpha, red, green and blue bytes.</summary>
    internal readonly struct Argb : IEquatable<Argb>
    {
        public Argb(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public bool Equals(Argb other) => A == other.A && R == other.R && G == other.G && B == other.B;
        public override bool Equals(object? obj) => obj is Argb other && Equals(other);
        public override int GetHashCode() => (A << 24) | (R << 16) | (G << 8) | B;
    }

    /// <summary>The colors of changed lines in the chat's diffs: pure green and red, translucent, less opaque on light themes where they glare.</summary>
    internal static class DiffPalette
    {
        public static Argb AddedLine(bool darkTheme) => new Argb(darkTheme ? (byte)0x73 : (byte)0x38, 0x00, 0xFF, 0x00);

        public static Argb RemovedLine(bool darkTheme) => new Argb(darkTheme ? (byte)0x8C : (byte)0x30, 0xFF, 0x00, 0x00);

        /// <summary>A changed word without a fill of Visual Studio's own takes its line's color, less transparent.</summary>
        public static Argb Stronger(Argb line) => new Argb((byte)Math.Min(255, line.A + 0x40), line.R, line.G, line.B);

        /// <summary>Whether a background color is dark, by its luminance.</summary>
        public static bool IsDark(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b < 128;
    }
}
