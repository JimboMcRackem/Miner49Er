namespace Miner49er.Core;

public enum EffectChannel { MoveSpeed, VisionRadius, BlastRadius }
public enum EffectKind { SpeedPotion, LongerVision, BiggerBlast, SlowMold }

public sealed class StatusEffect
{
    public EffectKind Kind { get; internal set; }
    public EffectChannel Channel { get; internal set; }
    public double Magnitude { get; internal set; }       // MoveSpeed: <1 faster, >1 slower
    public double RemainingSeconds { get; internal set; }
}
