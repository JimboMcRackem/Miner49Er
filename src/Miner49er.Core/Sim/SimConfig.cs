namespace Miner49er.Core;

public sealed class SimConfig
{
    public double PickaxeSeconds { get; set; } = 3.0;
    public double PlantSeconds { get; set; } = 1.0;
    public double FuseSeconds { get; set; } = 3.0;
    public int BlastRockRadius { get; set; } = 1;   // Manhattan radius of rock destruction
    public int BlastKillRadius { get; set; } = 1;   // Chebyshev radius that kills miners
    public int MaxLiveChargesPerMiner { get; set; } = 3;

    // Thrown dynamite: a lit stick lobbed in the facing direction that lands up to
    // ThrownDynamiteRange tiles away (or the last open tile before a wall/edge) and
    // detonates after a short, dodgeable fuse. Gated by DynamiteEnabled + a cooldown.
    public int ThrownDynamiteRange { get; set; } = 5;
    public double ThrownDynamiteFuseSeconds { get; set; } = 1.2;
    public double ThrownDynamiteCooldownSeconds { get; set; } = 3.0;

    // Global prize events (competitive only). Mode lets the sim pick mode-appropriate rewards.
    public GameMode Mode { get; set; } = GameMode.GoldRush;
    public bool PrizeEventsEnabled { get; set; } = false;
    public double PrizeFirstDelaySeconds { get; set; } = 150.0;
    public double PrizeIntervalSeconds  { get; set; } = 150.0;
    public double PrizeJitterSeconds    { get; set; } = 30.0;
    public double PrizeTelegraphSeconds { get; set; } = 5.0;
    public double PrizeExpirySeconds    { get; set; } = 60.0;
    public double PrizeMineSeconds      { get; set; } = 4.0;
    public double PrizeHoldSeconds      { get; set; } = 5.0;
    public int    PrizeHoldRadius       { get; set; } = 3;
    public int    PrizeMinPlayerSpacing { get; set; } = 4;
    public int    PrizeGoldReward       { get; set; } = 20;

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
    public double MoldSlowFactor { get; set; } = 2.5;    // move-cadence multiplier when stepped on (>1 = slower)
    public double MoldSlowSeconds { get; set; } = 3.0;   // how long the slow lingers after stepping on

    public double CrackDwellSeconds { get; set; } = 0.75;   // linger this long on a crack and it gives way

    public double LavaSpreadIntervalSeconds { get; set; } = 1.5;   // seconds per spread ring once a vent is breached
    public int LavaVentBudget { get; set; } = 8;                    // max tiles a single vent converts before going inert

    public int ReelSafeDistance { get; set; } = 3;   // min Manhattan tiles from charge to detonate
    public int ReelMaxLength    { get; set; } = 10;  // max tiles before the wire snaps and charge is lost

    // Determinism: seeds the sim's monster RNG (wander steps, goat re-aim).
    public int Seed { get; set; }

    // Monsters (Expedition). Cadence = seconds per one-tile step; lower is faster.
    public double MonsterSlimeMoveSeconds { get; set; } = 0.5;
    public double MonsterGhostMoveSeconds { get; set; } = 1.0;
    public double MonsterGoatMoveSeconds { get; set; } = 0.15;
    public double MonsterZombieMoveSeconds { get; set; } = 0.6;  // slower than slime; relentless
    public double MonsterSkeletonMoveSeconds     { get; set; } = 0.7;
    public double MonsterSkeletonDinoMoveSeconds { get; set; } = 1.2;
    public double MonsterWaterSnakeWaterMoveSeconds { get; set; } = 0.35;  // full speed in water
    public double MonsterWaterSnakeLandMoveSeconds  { get; set; } = 0.70;  // half speed on land
    public int MonsterSenseRadius { get; set; } = 6;   // Manhattan range at which a monster locks on

    public double PortalCooldownSeconds { get; set; } = 2.0; // per-pair lockout after a traversal

    // Manhattan radii within which each noise kind wakes a dormant skeleton.
    public int SkeletonExplosionWakeRadius { get; set; } = 12;
    public int SkeletonPickaxeWakeRadius   { get; set; } = 3;
    public int SkeletonStoneWakeRadius     { get; set; } = 8;
    public int SkeletonProximityWakeRadius { get; set; } = 4; // miner steps within this many tiles

    public int LanternRadius { get; set; } = 3;   // Chebyshev radius — ghosts inside are killed; ghosts won't enter
    public int LanternVisionBonus { get; set; } = 2;   // +tiles of fog radius while holding a lantern

    public double PermSpeedFactor    { get; set; } = 0.85;  // per-level move-cadence multiplier
    public int    PermVisionBonus    { get; set; } = 1;      // +tiles of fog radius per level
    public int    PermBlastBonus     { get; set; } = 1;      // +radius per level
    public int    MaxPermSpeedLevel  { get; set; } = 5;
    public int    MaxPermVisionLevel { get; set; } = 5;
    public int    MaxPermBlastLevel  { get; set; } = 3;

    public bool RequireChestForEscape { get; set; } = false;

    // When set, the escape opens when this idol is picked up (not on 50% gold).
    public ItemKind? ExpeditionTreasureKind { get; set; } = null;
    public bool DynamiteEnabled { get; set; } = true;
    public bool TreasureHuntMode { get; set; } = false;

    public float MonsterCountMultiplier { get; set; } = 1.0f;

    // Floor traps — plant dynamite on floor tiles; triggers when a miner steps on it.
    public bool TripMinesEnabled { get; set; } = false;

    // LMS/Derby: players may pickaxe floor tiles to create cracks.
    public bool UnstableFloorEnabled { get; set; } = false;

    // Ceiling rock falls — drops Rock tiles when gold drops to ≤ 5%.
    public bool RockFallsEnabled { get; set; } = false;
    public double RockFallMinDelay { get; set; } = 2.0;
    public double RockFallMaxDelay { get; set; } = 4.0;
    public double RockFallIntervalSeconds { get; set; } = 6.0;
}
