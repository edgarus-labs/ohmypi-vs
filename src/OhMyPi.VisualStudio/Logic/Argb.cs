using System;

namespace OhMyPi.VisualStudio.Logic;

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
