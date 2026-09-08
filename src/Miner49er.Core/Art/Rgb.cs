using System;

namespace Miner49er.Core;

/// <summary>An 8-bit RGB colour with no engine dependency, so palette and tint logic
/// can be unit-tested without Godot. <c>game/art/C64Colors.cs</c> adapts these to
/// <c>Godot.Color</c> at the boundary.</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public Rgb(int r, int g, int b) : this((byte)r, (byte)g, (byte)b) { }

    /// <summary>Rec.601 luma, 0.0-1.0. Matches tools/artpass/palette.py:luminance.</summary>
    public float Luminance => (0.299f * R + 0.587f * G + 0.114f * B) / 255f;

    /// <summary>HSV saturation, 0.0-1.0. Matches Godot's Color.S.</summary>
    public float Saturation
    {
        get
        {
            int max = Math.Max(R, Math.Max(G, B));
            int min = Math.Min(R, Math.Min(G, B));
            return max == 0 ? 0f : (max - min) / (float)max;
        }
    }

    /// <summary>HSV hue, 0.0-1.0. Matches Godot's Color.H.</summary>
    public float Hue
    {
        get
        {
            float r = R / 255f, g = G / 255f, b = B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            if (d <= 0f) return 0f;

            float h;
            if (max == r) h = (g - b) / d % 6f;
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;

            h /= 6f;
            return h < 0f ? h + 1f : h;
        }
    }

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is Rgb o && Equals(o);

    public override int GetHashCode() => (R << 16) | (G << 8) | B;

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);

    public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
}
