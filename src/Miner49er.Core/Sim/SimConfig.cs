namespace Miner49er.Core;

public sealed class SimConfig
{
    public double PickaxeSeconds { get; set; } = 6.0;
    public double PlantSeconds { get; set; } = 1.0;
    public double FuseSeconds { get; set; } = 3.0;
    public int BlastRockRadius { get; set; } = 1;   // Manhattan radius of rock destruction
    public int BlastKillRadius { get; set; } = 1;   // Chebyshev radius that kills miners
    public int MaxLiveChargesPerMiner { get; set; } = 3;
}
