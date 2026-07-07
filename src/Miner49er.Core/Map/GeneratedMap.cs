namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
    public required IReadOnlyList<GridPos> Decoys { get; init; }
    public GridPos? EscapeTile { get; init; }
    public GridPos? ShopPos { get; init; }
    public GridPos? ExpeditionTreasurePos { get; init; }
}
