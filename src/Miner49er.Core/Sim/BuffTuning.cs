namespace Miner49er.Core;

/// <summary>Tuning for the temporary floor-pickup buffs. Durations are authored in
/// minutes; the *Seconds accessors feed Simulation.ApplyEffect (which takes seconds).
/// Magnitudes match the EffectChannel each buff drives: vision/blast add tiles/radius,
/// speed is a move-cadence multiplier where &lt;1 is faster.</summary>
public static class BuffTuning
{
    public const int    VisionMagnitude = 3;      // +tiles of fog radius
    public const int    BlastMagnitude  = 1;      // +explosion radius
    public const double SpeedMagnitude  = 0.80;   // move-cadence multiplier (<1 = faster)

    public const double VisionMinutes = 2.0;
    public const double BlastMinutes  = 2.0;
    public const double SpeedMinutes  = 2.0;

    public static double VisionSeconds => VisionMinutes * 60.0;
    public static double BlastSeconds  => BlastMinutes  * 60.0;
    public static double SpeedSeconds  => SpeedMinutes  * 60.0;
}
