namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank }

public static class TileTypeExtensions
{
    /// <summary>Multiplier applied to a miner's move cadence while on shallow water.</summary>
    public const double ShallowSlowFactor = 2.0;

    /// <summary>Safe to stand on (used for spawns, fog, drip placement, reachability).</summary>
    public static bool IsWalkable(this TileType t) => t is TileType.Floor or TileType.ShallowWater or TileType.Plank;

    /// <summary>A miner may move onto this tile. Deep water is enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank;

    /// <summary>Entering this tile kills the miner (drowning).</summary>
    public static bool IsLethal(this TileType t) => t == TileType.DeepWater;

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
}
