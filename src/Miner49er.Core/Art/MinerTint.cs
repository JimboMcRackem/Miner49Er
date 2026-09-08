using System;

namespace Miner49er.Core;

/// <summary>Recolours a miner sprite pixel into a team's ramp.
///
/// Replaces the old continuous multiply in MatchClient.TintMiner, which produced
/// arbitrary RGB and so broke the 16-colour guarantee at runtime. Banding into an
/// authored ramp keeps every output pixel in-palette.</summary>
public static class MinerTint
{
    /// <summary>True when a pixel keeps its own colour instead of taking the team's.
    ///
    /// Source sprites are guaranteed to contain only palette colours, and the flesh
    /// ramp puts skin on lightred and the hard hat on yellow, so this is exact set
    /// membership rather than a hue/saturation heuristic.
    ///
    /// It used to be `hue &lt; 0.14 &amp;&amp; saturation &gt; 0.18 &amp;&amp; luminance &gt; 0.5`, thresholds
    /// calibrated against the pre-conversion art. Once sprites were converted, both
    /// identity colours fell outside them at once - lightred is faintly magenta so its
    /// hue wraps to 0.991, and palette yellow sits at 0.172 - which preserved nothing
    /// and rendered the whole miner, skin included, in the team colour.</summary>
    public static bool IsHuePreserved(Rgb px) =>
        Array.IndexOf(C64Ramps.TintPreserve, px) >= 0;

    /// <summary>Map one sprite pixel into the team ramp, preserving skin and hard hat.</summary>
    public static Rgb Apply(Rgb source, ColorRamp teamRamp) =>
        IsHuePreserved(source) ? source : teamRamp.Map(source.Luminance);
}
