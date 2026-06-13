using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorPitTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed, bool pits) =>
        new() { Seed = seed, PlayerCount = 4, Pits = pits };

    private static List<GridPos> PitsOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) == TileType.Pit).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void No_pits_when_toggle_is_off(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: false)).Grid;
        Assert.Empty(PitsOf(grid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_are_generated_when_toggle_is_on(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        Assert.NotEmpty(PitsOf(grid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Generation_is_deterministic_with_pits(int seed)
    {
        var a = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        var b = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        Assert.Equal(PitsOf(a), PitsOf(b));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_never_touch_the_impermeable_border(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        foreach (var p in PitsOf(grid))
            Assert.False(p.X == 0 || p.Y == 0 || p.X == grid.Width - 1 || p.Y == grid.Height - 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_never_sit_on_spawns_center_or_items(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, pits: true));
        var pitSet = new HashSet<GridPos>(PitsOf(map.Grid));
        Assert.DoesNotContain(map.Center, pitSet);
        foreach (var s in map.Spawns) Assert.DoesNotContain(s, pitSet);
        foreach (var it in map.Items) Assert.DoesNotContain(it.Pos, pitSet);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Spawns_can_still_reach_center_with_pits(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, pits: true));
        var g = map.Grid;

        // BFS over safe ground (walkable: Floor/Shallow/Plank — pits excluded).
        var seen = new HashSet<GridPos> { map.Spawns[0] };
        var q = new Queue<GridPos>();
        q.Enqueue(map.Spawns[0]);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n).IsWalkable() && seen.Add(n))
                    q.Enqueue(n);
            }
        }

        Assert.Contains(map.Center, seen);
        foreach (var s in map.Spawns) Assert.Contains(s, seen);
    }
}
