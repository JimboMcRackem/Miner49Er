using System;

namespace Miner49er.Core;

/// <summary>An authored dark-to-light run of C64 palette colours, plus the absolute
/// luminance thresholds that divide them. Mapping by fixed thresholds (rather than each
/// image's own brightness range) is what keeps animation frames from shimmering.</summary>
public sealed class ColorRamp
{
    public readonly Rgb[] Colors;

    /// <summary>Ascending luminance thresholds; always <c>Colors.Length - 1</c> of them.</summary>
    public readonly float[] Edges;

    public ColorRamp(Rgb[] colors, float[] edges)
    {
        if (colors.Length < 2)
            throw new ArgumentException("a ramp needs at least two colours", nameof(colors));
        if (edges.Length != colors.Length - 1)
            throw new ArgumentException(
                $"expected {colors.Length - 1} edges for {colors.Length} colours, got {edges.Length}",
                nameof(edges));

        Colors = colors;
        Edges = edges;
    }

    /// <summary>The brightest colour — what identifies this ramp to a player.</summary>
    public Rgb Identity => Colors[Colors.Length - 1];

    /// <summary>Map a 0.0-1.0 luminance to its band. A value exactly on an edge takes
    /// the upper band, matching tools/artpass/palette.py:band_index.</summary>
    public Rgb Map(float luminance)
    {
        int i = 0;
        foreach (var e in Edges)
        {
            if (luminance >= e) i++;
            else break;
        }
        return Colors[i];
    }
}
