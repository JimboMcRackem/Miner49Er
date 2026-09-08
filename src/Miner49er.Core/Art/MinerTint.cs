namespace Miner49er.Core;

/// <summary>Recolours a miner sprite pixel into a team's ramp.
///
/// Replaces the old continuous multiply in MatchClient.TintMiner, which produced
/// arbitrary RGB and so broke the 16-colour guarantee at runtime. Banding into an
/// authored ramp keeps every output pixel in-palette.</summary>
public static class MinerTint
{
    // Warm hue, reasonably saturated, and bright: skin and the yellow hard hat.
    // Thresholds carried over unchanged from the original TintMiner so the variants'
    // lighter, less-saturated skin tones keep their colour.
    private const float MaxWarmHue = 0.14f;
    private const float MinSaturation = 0.18f;
    private const float MinLuminance = 0.5f;

    /// <summary>True when a pixel keeps its own colour instead of taking the team's.</summary>
    public static bool IsHuePreserved(Rgb px) =>
        px.Hue < MaxWarmHue && px.Saturation > MinSaturation && px.Luminance > MinLuminance;

    /// <summary>Map one sprite pixel into the team ramp, preserving skin and hard hat.</summary>
    public static Rgb Apply(Rgb source, ColorRamp teamRamp) =>
        IsHuePreserved(source) ? source : teamRamp.Map(source.Luminance);
}
