using System;

namespace Miner49er.Core;

/// <summary>Occasional-guttering brightness multiplier for a light source. Pure and
/// deterministic in (seed, t, profile): mostly 1.0, with sparse raised-cosine dips.
/// The caller supplies the time, so there is no wall-clock read or mutable state here.
/// Client-side cosmetic only — never used by the simulation.</summary>
public static class Flicker
{
    /// <param name="WindowSeconds">Length of each independent event window.</param>
    /// <param name="GutterChance">0..1 probability a given window contains a dip.</param>
    /// <param name="DipDepth">0..1 fraction subtracted at the dip's deepest point.</param>
    /// <param name="DipWidthSeconds">Full width of the raised-cosine dip.</param>
    /// <param name="FloorLevel">The multiplier never drops below this.</param>
    public readonly record struct Profile(
        double WindowSeconds,
        double GutterChance,
        double DipDepth,
        double DipWidthSeconds,
        double FloorLevel);

    // Fire: lanterns + lava/vents — deeper, snappier dips.
    public static readonly Profile Fire = new(1.6, 0.5, 0.5, 0.25, 0.5);
    // Crystal: CrystalRock + CrystalShard — gentler, slower (magical, not flame).
    public static readonly Profile Crystal = new(2.6, 0.4, 0.25, 0.5, 0.75);

    /// <summary>Brightness multiplier at time <paramref name="t"/> for a source
    /// identified by <paramref name="seed"/>. In [p.FloorLevel, 1.0].</summary>
    public static float Multiplier(int seed, double t, Profile p)
    {
        if (p.WindowSeconds <= 0.0 || p.DipWidthSeconds <= 0.0) return 1f;

        long k = (long)Math.Floor(t / p.WindowSeconds);
        double half = p.DipWidthSeconds * 0.5;
        double maxDip = 0.0;

        // Check this window and its neighbours so a dip straddling a boundary isn't clipped.
        for (long w = k - 1; w <= k + 1; w++)
        {
            if (Hash01(seed, w, 0x9E3779B1u) >= p.GutterChance) continue;   // no event this window
            double centre = w * p.WindowSeconds + Hash01(seed, w, 0x85EBCA77u) * p.WindowSeconds;
            double d = t - centre;
            if (d <= -half || d >= half) continue;                          // outside this dip

            // Raised cosine: 1 at the centre, smoothly 0 at ±half.
            double shape = 0.5 * (1.0 + Math.Cos(Math.PI * d / half));
            double dip = shape * p.DipDepth;
            if (dip > maxDip) maxDip = dip;
        }

        double m = 1.0 - maxDip;
        if (m < p.FloorLevel) m = p.FloorLevel;
        if (m > 1.0) m = 1.0;
        return (float)m;
    }

    // Deterministic (seed, window) -> [0,1). `salt` yields independent draws from one window.
    private static double Hash01(int seed, long window, uint salt)
    {
        ulong x = (ulong)(uint)seed;
        x ^= (ulong)window * 0x9E3779B97F4A7C15UL;
        x ^= salt + 0x165667B19E3779F9UL;
        // splitmix64 finalizer
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / 9007199254740992.0); // top 53 bits → [0,1)
    }
}
