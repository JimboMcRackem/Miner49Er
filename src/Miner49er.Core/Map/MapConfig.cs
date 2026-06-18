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
    public int GoldVeinCount { get; set; } = 8;
    public int BaseItemCount { get; set; } = 9;   // items on the base map
    public int ItemsPerPlayer { get; set; } = 1;  // light scaling with player count / map growth
    public int VisibleItemCount { get; set; } = 2;  // of the total, this many are visible toolboxes; rest are buried
    public int DecoyCount { get; set; } = 4;        // empty "suspicious spots" that shimmer under Listen but hold nothing
    public int WaterPlankCount { get; set; } = 3;   // visible carried water-planks scattered on Floor
    public int SlowMoldCount { get; set; } = 3;     // visible carried slow-molds scattered on Floor
    public int LanternCount { get; set; } = 1;      // visible carried lanterns scattered on Floor
    public int ChestCount { get; set; } = 0;        // visible Chest toolboxes per floor

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
                                bool pits = false, bool caveIns = false, bool lava = false, int mapScale = 1)
    {
        var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount, Pits = pits, CaveIns = caveIns, Lava = lava };
        if (mode == GameMode.ReachCenter)
        {
            cfg.BaseWidth = 40;
            cfg.BaseHeight = 40;
            cfg.InitialFloorChance = 0.42f;
        }
        if (mapScale > 1)
        {
            cfg.BaseWidth  = 24 + (mapScale - 1) * 8;
            cfg.BaseHeight = 24 + (mapScale - 1) * 8;
            float areaFactor = (float)(cfg.BaseWidth * cfg.BaseHeight) / (24f * 24f);
            cfg.GoldVeinCount = (int)System.Math.Round(cfg.GoldVeinCount * areaFactor);
        }
        return cfg;
    }

    /// <summary>Deterministic difficulty curve for Expedition dungeon floors 1–20.
    /// Size and hazards escalate in four bands; only the seed varies the layout.</summary>
    public static MapConfig FloorConfig(int floor, int seed)
    {
        int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, _ => 4 };
        bool pits    = floor >= 6;
        bool caveIns = floor >= 11;
        bool lava    = floor >= 16;
        var cfg = For(GameMode.Expedition, seed, 1, pits, caveIns, lava, mapScale);
        cfg.ChestCount = floor <= 10 ? 1 : 2;
        return cfg;
    }
}
