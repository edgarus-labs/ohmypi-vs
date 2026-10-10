using System;

namespace OhMyPi.VisualStudio.Logic;

/// <summary>A color as alpha, red, green and blue bytes.</summary>
internal readonly struct Argb : IEquatable<Argb>
{
    /// <summary>
    /// Initializes a new instance of the Argb struct using the specified alpha, red, green, and blue color components.
    /// </summary>
    /// <param name="a">The a.</param>
    /// <param name="r">The r.</param>
    /// <param name="g">The g.</param>
    /// <param name="b">The b.</param>
    public Argb(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    /// <summary>
    /// Gets the a.
    /// </summary>
    public byte A { get; }

    /// <summary>
    /// Gets the r.
    /// </summary>
    public byte R { get; }

    /// <summary>
    /// Gets the g.
    /// </summary>
    public byte G { get; }

    /// <summary>
    /// Gets the b.
    /// </summary>
    public byte B { get; }

    /// <summary>
    /// Determines whether the current ARGB color instance is equal to another ARGB color instance by comparing their alpha, red, green, and blue components.
    /// </summary>
    /// <param name="other">The other.</param>
    /// <returns>true if the operation succeeded; otherwise, false.</returns>
    public bool Equals(Argb other) => A == other.A && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is Argb other && Equals(other);

    /// <summary>
    /// Returns a hash code calculated by packing the ARGB color components into a single 32-bit integer.
    /// </summary>
    /// <returns>The int result.</returns>
    public override int GetHashCode() => (A << 24) | (R << 16) | (G << 8) | B;
}
