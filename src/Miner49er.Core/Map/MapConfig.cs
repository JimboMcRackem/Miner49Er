namespace Miner49er.Core;

public sealed class MapConfig
{
    public int Seed { get; set; }
    public int PlayerCount { get; set; } = 1;
    public int BaseWidth { get; set; } = 32;
    public int BaseHeight { get; set; } = 32;
    public int SizePerPlayer { get; set; } = 8;
    public float InitialFloorChance { get; set; } = 0.45f;
    public int SmoothingSteps { get; set; } = 4;
    public int GoldVeinCount { get; set; } = 14;
    public int BaseItemCount { get; set; } = 9;   // items on the base map
    public int ItemsPerPlayer { get; set; } = 1;  // light scaling with player count / map growth
    public int VisibleItemCount { get; set; } = 2;  // of the total, this many are visible toolboxes; rest are buried
    public int DecoyCount { get; set; } = 4;        // empty "suspicious spots" that shimmer under Listen but hold nothing
    public int WaterPlankCount { get; set; } = 3;   // visible carried water-planks scattered on Floor
    public int SlowMoldCount { get; set; } = 3;     // visible carried slow-molds scattered on Floor
    public int LanternCount { get; set; } = 1;      // visible carried lanterns scattered on Floor
    public int DetonatorCount { get; set; } = 0;    // visible carried detonators scattered on Floor
    public int ChestCount { get; set; } = 0;        // visible Chest toolboxes per floor
    public bool HasShop { get; set; } = false;     // place a shopkeeper tile on this floor
    public ItemKind[]? BuriedIdolKinds { get; set; } = null; // TreasureHunt: idol kinds to bury in rock

    // Expedition treasure gate — every 4th floor hides a mythical idol that opens the exit.
    public ItemKind? ExpeditionTreasureKind    { get; set; } = null;
    public bool      ExpeditionTreasureInChest { get; set; } = false;  // false = buried in wall, true = toolbox on floor

    // Flooding — when true, map generator places the escape tile (spawns[0]) in the
    // map interior so it floods last rather than at the corner where it floods first.
    public bool Flooding { get; set; } = false;

    // Flooded cave — converts ~80% of floor tiles to shallow/deep water after item placement,
    // leaving only small islands of dry land. Assigned by FloorConfig from floor 10 onward (~1/5).
    public bool  FloodedCave         { get; set; } = false;
    public float FloodedCaveDryRatio { get; set; } = 0.20f;

    public bool SealCenter { get; set; } = false; // ring center with Rock so ReachCenter requires digging

    // Bottomless pits (Phase 4d) — host lobby toggle, off by default.
    public bool Pits { get; set; } = false;            // gates the whole PlacePits pass
    public int PitSiteCount { get; set; } = 6;          // base number of pit sites (light per-player scaling)
    public double PitClusterChance { get; set; } = 0.3; // chance a site grows beyond one tile
    public int PitClusterMax { get; set; } = 5;         // max tiles in a grown cluster

    // Cave-ins (Phase 4d) — host lobby toggle, off by default.
    public bool CaveIns { get; set; } = false;             // gates the whole PlaceCracks pass
    public int CrackSiteCount { get; set; } = 4;            // base number of crack patches (light per-player scaling)
    public int CrackPatchMax { get; set; } = 8;            // max tiles in a grown patch ("areas", larger than pits)
    public double CrackPatchGrowChance { get; set; } = 0.7; // chance a site grows beyond one tile

    // Lava (Phase 4d) — host lobby toggle, off by default.
    public bool Lava { get; set; } = false;              // gates both lava passes
    public int LavaPoolCount { get; set; } = 3;          // static lethal pools/lines
    public int LavaPoolMax { get; set; } = 6;            // max tiles in a grown pool
    public double LavaPoolGrowChance { get; set; } = 0.6;
    public int LavaVentCount { get; set; } = 3;          // buried vents (light per-player scaling)

    // Water generation (Phase 4a).
    public int PoolCount { get; set; } = 3;
    public int PoolRadiusMin { get; set; } = 2;
    public int PoolRadiusMax { get; set; } = 4;
    public int RiverCount { get; set; } = 2;
    public int RiverLengthMin { get; set; } = 12;
    public int RiverLengthMax { get; set; } = 30;
    public float DeepWaterChance { get; set; } = 0.6f;

    /// <summary>Builds a map config tuned for the given mode. Reach Center gets a
    /// larger, less-open map so the run to the centre is a real journey; other
    /// modes keep the base settings. Deterministic from (mode, seed, playerCount),
    /// so host and clients regenerate identical maps.</summary>
    public static MapConfig For(GameMode mode, int seed, int playerCount,
                                bool pits = false, bool caveIns = false, bool lava = false,
                                int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite)
    {
        var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount, Pits = pits, CaveIns = caveIns, Lava = lava };
        if (mode == GameMode.ReachCenter)
        {
            cfg.BaseWidth = 64;
            cfg.BaseHeight = 64;
            cfg.InitialFloorChance = 0.42f;
            cfg.SealCenter = true;
        }
        if (mapScale > 1)
        {
            cfg.BaseWidth  = 32 + (mapScale - 1) * 16;
            cfg.BaseHeight = 32 + (mapScale - 1) * 16;
            float areaFactor = (float)(cfg.BaseWidth * cfg.BaseHeight) / (32f * 32f);
            cfg.GoldVeinCount   = (int)System.Math.Round(cfg.GoldVeinCount * areaFactor);
            cfg.BaseItemCount   = (int)System.Math.Round(cfg.BaseItemCount * areaFactor);
            cfg.ItemsPerPlayer  = (int)System.Math.Max(1, System.Math.Round(cfg.ItemsPerPlayer * areaFactor));
            cfg.WaterPlankCount = (int)System.Math.Round(cfg.WaterPlankCount * areaFactor);
            cfg.SlowMoldCount   = (int)System.Math.Round(cfg.SlowMoldCount * areaFactor);
        }
        if (mode == GameMode.TreasureHunt)
        {
            cfg.GoldVeinCount    = 0;
            cfg.ChestCount       = 0;
            cfg.BaseItemCount    = 0;
            cfg.VisibleItemCount = 0;
            cfg.BuriedIdolKinds  = TreasureAssignment.AllAssigned(seed, playerCount);
        }
        if (mode == GameMode.DemolitionDerby)
        {
            cfg.GoldVeinCount    = 0;
            cfg.BaseItemCount    = 0;
            cfg.VisibleItemCount = 0;
            cfg.WaterPlankCount  = 0;
            cfg.SlowMoldCount    = 0;
            cfg.LanternCount     = 0;
            cfg.ChestCount       = 0;
            cfg.DecoyCount       = 0;
        }
        cfg.DetonatorCount = explosive == ExplosiveMode.Dynamite ? 0 : playerCount;
        return cfg;
    }

    /// <summary>Deterministic difficulty curve for Expedition dungeon floors 1–50.
    /// Size and hazards escalate in seven bands; only the seed varies the layout.</summary>
    public static MapConfig FloorConfig(int floor, int seed, int playerCount = 1)
    {
        int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, <= 20 => 4, <= 30 => 5, <= 40 => 6, _ => 7 };
        bool pits    = floor >= 6;
        bool caveIns = floor >= 11;
        bool lava    = floor >= 16;
        var cfg = For(GameMode.Expedition, seed, playerCount, pits, caveIns, lava, mapScale);
        cfg.ChestCount = floor <= 10 ? 1 : 2;
        cfg.HasShop = floor % 4 == 0;
        cfg.DetonatorCount = floor >= 3 ? 1 : 0;
        cfg.FloodedCave = floor >= 10 && (uint)HashCode.Combine(seed, floor) % 5 == 0;
        if (floor % 4 == 0)
        {
            var treasureRng = new System.Random(HashCode.Combine(seed, floor, 0xFEED));
            var allIdols    = TreasureAssignment.AllIdols();
            cfg.ExpeditionTreasureKind    = allIdols[treasureRng.Next(allIdols.Length)];
            cfg.ExpeditionTreasureInChest = treasureRng.Next(2) == 0;
        }
        return cfg;
    }
}
