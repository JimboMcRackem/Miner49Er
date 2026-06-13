using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCaveInTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed, bool caveIns) =>
        new() { Seed = seed, PlayerCount = 4, CaveIns = caveIns };

    private static List<GridPos> CracksOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) is TileType.Cracked or TileType.Crumbling).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void No_cracks_when_toggle_is_off(int seed)
        => Assert.Empty(CracksOf(MapGenerator.Generate(Config(seed, caveIns: false)).Grid));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Cracks_are_generated_when_toggle_is_on(int seed)
        => Assert.NotEmpty(CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Generation_is_deterministic_with_cracks(int seed)
    {
        var a = CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid);
        var b = CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Cracks_only_replace_floor_never_spawns_center_or_items(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, caveIns: true));
        var crackSet = new HashSet<GridPos>(CracksOf(map.Grid));
        Assert.DoesNotContain(map.Center, crackSet);
        foreach (var s in map.Spawns) Assert.DoesNotContain(s, crackSet);
        foreach (var it in map.Items) Assert.DoesNotContain(it.Pos, crackSet);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Initial_map_stays_connected_spawns_reach_center(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, caveIns: true));
        var g = map.Grid;
        // Cracks are walkable at gen time, so connectivity must hold over walkable tiles.
        var seen = new HashSet<GridPos> { map.Spawns[0] };
        var q = new Queue<GridPos>();
        q.Enqueue(map.Spawns[0]);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n).IsWalkable() && seen.Add(n)) q.Enqueue(n);
            }
        }
        Assert.Contains(map.Center, seen);
        foreach (var s in map.Spawns) Assert.Contains(s, seen);
    }
}
