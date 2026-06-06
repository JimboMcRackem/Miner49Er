namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock }

public static class TileTypeExtensions
{
    public static bool IsWalkable(this TileType t) => t == TileType.Floor;
    public static bool IsMinable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
    public static bool IsBlastable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
}
