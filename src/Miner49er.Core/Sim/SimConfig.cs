namespace Miner49er.Core;

public sealed class SimConfig
{
    public double PickaxeSeconds { get; set; } = 6.0;
    public double PlantSeconds { get; set; } = 1.0;
    public double FuseSeconds { get; set; } = 3.0;
    public int BlastRockRadius { get; set; } = 1;   // Manhattan radius of rock destruction
    public int BlastKillRadius { get; set; } = 1;   // Chebyshev radius that kills miners
    public int MaxLiveChargesPerMiner { get; set; } = 3;

    public double BaseMoveSeconds { get; set; } = 0.12;  // Standard preset (seconds per tile)
    public double MinMoveSeconds { get; set; } = 0.05;   // clamp floor — no teleporting
    public double MaxMoveSeconds { get; set; } = 0.40;   // clamp ceiling — never frozen

    public int VisionRadius { get; set; } = 5;   // base fog radius (migrated from MatchClient)

    public double SpeedPotionFactor { get; set; } = 0.6;   // move-cadence multiplier while active
    public double SpeedPotionSeconds { get; set; } = 8.0;
    public int VisionBonus { get; set; } = 3;              // +tiles of fog radius while active
    public double VisionSeconds { get; set; } = 12.0;
    public int BlastBonus { get; set; } = 1;               // +radius on charges planted while active
    public double BlastSeconds { get; set; } = 12.0;
}
