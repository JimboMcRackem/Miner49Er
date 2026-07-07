namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit, Cracked, Crumbling, Lava, LavaVent }

public static class TileTypeExtensions
{
    /// <summary>Multiplier applied to a miner's move cadence while on shallow water.</summary>
    public const double ShallowSlowFactor = 2.0;

    /// <summary>Safe to stand on (used for spawns, fog, drip placement, reachability).
    /// Cracks are floor you stand on; they only give way on a second loading or via dwell.</summary>
    public static bool IsWalkable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.Plank
          or TileType.Cracked or TileType.Crumbling;

    /// <summary>A miner may move onto this tile. Deep water, pits, and lava are enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank
          or TileType.Pit or TileType.Cracked or TileType.Crumbling
          or TileType.Lava or TileType.LavaVent;

    /// <summary>Entering this tile kills the miner (drowning, falling into a pit, or burning in lava).</summary>
    public static bool IsLethal(this TileType t) =>
        t is TileType.DeepWater or TileType.Pit or TileType.Lava or TileType.LavaVent;

    /// <summary>Move-cadence multiplier for the tile a miner is standing on.</summary>
    public static double MoveCostMultiplier(this TileType t) =>
        t == TileType.ShallowWater ? ShallowSlowFactor : 1.0;

    public static bool IsMinable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
    public static bool IsBlastable(this TileType t) => t is TileType.Rock or TileType.GoldRock;

    /// <summary>Blocks line-of-sight (the rock family). Floor, water, and planks are transparent.</summary>
    public static bool BlocksSight(this TileType t) =>
        t is TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock;

    /// <summary>Shallow or deep water (used by water placement and the flood).</summary>
    public static bool IsWater(this TileType t) => t is TileType.ShallowWater or TileType.DeepWater;

    /// <summary>Open traversable space — floor or shallow water. Used for region detection and monster spawning.</summary>
    public static bool IsTraversable(this TileType t) => t is TileType.Floor or TileType.ShallowWater;

    /// <summary>A held water-plank can be laid here (water, a pit, or a crack) to form a safe Plank tile.</summary>
    public static bool IsBridgeable(this TileType t) =>
        t.IsWater() || t is TileType.Pit or TileType.Cracked or TileType.Crumbling;
}
