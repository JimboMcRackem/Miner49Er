namespace Miner49er.Core;

public sealed class SimConfig
{
    public double PickaxeSeconds { get; set; } = 3.0;
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

    public double MoldSeconds { get; set; } = 20.0;      // patch lifetime before it decays
    public int MoldRadius { get; set; } = 2;             // Manhattan radius of the dropped patch spread
    public double MoldSlowFactor { get; set; } = 1.6;    // move-cadence multiplier when stepped on (>1 = slower)
    public double MoldSlowSeconds { get; set; } = 3.0;   // how long the slow lingers after stepping on

    public double CrackDwellSeconds { get; set; } = 0.75;   // linger this long on a crack and it gives way

    public double LavaSpreadIntervalSeconds { get; set; } = 1.5;   // seconds per spread ring once a vent is breached
    public int LavaVentBudget { get; set; } = 8;                    // max tiles a single vent converts before going inert

    public int ReelSafeDistance { get; set; } = 5;   // min Manhattan tiles from charge to detonate
    public int ReelMaxLength    { get; set; } = 10;  // max tiles before the wire snaps and charge is lost

    // Determinism: seeds the sim's monster RNG (wander steps, goat re-aim).
    public int Seed { get; set; }

    // Monsters (Expedition). Cadence = seconds per one-tile step; lower is faster.
    public double MonsterSlimeMoveSeconds { get; set; } = 0.5;
    public double MonsterGhostMoveSeconds { get; set; } = 1.0;
    public double MonsterGoatMoveSeconds { get; set; } = 0.15;
    public double MonsterZombieMoveSeconds { get; set; } = 0.6;  // slower than slime; relentless
    public int MonsterSenseRadius { get; set; } = 6;   // Manhattan range at which a monster locks on

    public int LanternRadius { get; set; } = 3;   // Chebyshev radius — ghosts inside are killed; ghosts won't enter
    public int LanternVisionBonus { get; set; } = 2;   // +tiles of fog radius while holding a lantern

    public double PermSpeedFactor    { get; set; } = 0.85;  // per-level move-cadence multiplier
    public int    PermVisionBonus    { get; set; } = 1;      // +tiles of fog radius per level
    public int    PermBlastBonus     { get; set; } = 1;      // +radius per level
    public int    MaxPermSpeedLevel  { get; set; } = 5;
    public int    MaxPermVisionLevel { get; set; } = 5;
    public int    MaxPermBlastLevel  { get; set; } = 3;

    public bool RequireChestForEscape { get; set; } = false;
    public bool DynamiteEnabled { get; set; } = true;
    public bool TreasureHuntMode { get; set; } = false;

    public float MonsterCountMultiplier { get; set; } = 1.0f;

    // Floor traps — plant dynamite on floor tiles; triggers when a miner steps on it.
    public bool TripMinesEnabled { get; set; } = false;

    // Ceiling rock falls — drops Rock tiles when gold drops to ≤ 5%.
    public bool RockFallsEnabled { get; set; } = false;
    public double RockFallMinDelay { get; set; } = 2.0;
    public double RockFallMaxDelay { get; set; } = 4.0;
    public double RockFallIntervalSeconds { get; set; } = 6.0;
}
