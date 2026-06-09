namespace Miner49er.Core;

public enum EffectChannel { MoveSpeed }            // 4c-2 adds MiningSpeed, VisionRadius, …
public enum EffectKind { DebugSpeed, DebugSlow }   // 4c-2 replaces these with SpeedPotion, SlowMold, …

public sealed class StatusEffect
{
    public EffectKind Kind { get; internal set; }
    public EffectChannel Channel { get; internal set; }
    public double Magnitude { get; internal set; }       // MoveSpeed: <1 faster, >1 slower
    public double RemainingSeconds { get; internal set; }
}
