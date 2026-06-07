namespace Miner49er.Core;

public sealed class MapConfig
{
    public int Seed { get; set; }
    public int PlayerCount { get; set; } = 1;
    public int BaseWidth { get; set; } = 24;
    public int BaseHeight { get; set; } = 24;
    public int SizePerPlayer { get; set; } = 6;
    public float InitialFloorChance { get; set; } = 0.45f;
    public int SmoothingSteps { get; set; } = 4;
    public int MinSpawnDistance { get; set; } = 6;
    public int GoldVeinCount { get; set; } = 8;

    // Water generation (Phase 4a).
    public int PoolCount { get; set; } = 3;
    public int PoolRadiusMin { get; set; } = 2;
    public int PoolRadiusMax { get; set; } = 4;
    public int RiverCount { get; set; } = 2;
    public int RiverLengthMin { get; set; } = 12;
    public int RiverLengthMax { get; set; } = 30;
    public float DeepWaterChance { get; set; } = 0.6f;
}
