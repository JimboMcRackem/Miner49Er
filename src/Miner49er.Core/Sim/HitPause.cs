namespace Miner49er.Core;

/// <summary>A brief freeze of on-screen motion when a blow lands. Purely visual: the sim keeps
/// running, so a pause on the host never stalls the other players.</summary>
public sealed class HitPause
{
    private double _remaining;

    public bool Active => _remaining > 0;

    public void Trigger(double seconds) => _remaining = Math.Max(_remaining, seconds);

    /// <summary>The time motion should advance this frame: none while paused.</summary>
    public double Scale(double dt)
    {
        if (_remaining < dt) { _remaining = 0; return dt; }
        _remaining -= dt;
        return 0.0;
    }
}
