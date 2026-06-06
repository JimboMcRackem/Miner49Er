namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
}
