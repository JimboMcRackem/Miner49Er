namespace Miner49er.Core;

/// <summary>How hard a tremor is shaking, 0 to 1: it eases up over the ramp, holds at the
/// peak, then eases back down. Shake, dust, sound and the slowdown all follow this.</summary>
public static class TremorCurve
{
    public static double Intensity(double elapsed, double seconds, double ramp)
    {
        double rise = Math.Min(elapsed, seconds - elapsed) / ramp;
        double t = Math.Clamp(rise, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
