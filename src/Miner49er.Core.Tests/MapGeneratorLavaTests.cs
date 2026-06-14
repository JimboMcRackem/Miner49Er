using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorLavaTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed, bool lava) =>
        new() { Seed = seed, PlayerCount = 4, Lava = lava };

    private static List<GridPos> LavaOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) == TileType.Lava).ToList();

    private static List<GridPos> VentsOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) == TileType.LavaVent).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void No_lava_or_vents_when_toggle_is_off(int seed)
    {
        var g = MapGenerator.Generate(Config(seed, lava: false)).Grid;
        Assert.Empty(LavaOf(g));
        Assert.Empty(VentsOf(g));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Static_lava_and_vents_are_generated_when_toggle_is_on(int seed)
    {
        var g = MapGenerator.Generate(Config(seed, lava: true)).Grid;
        Assert.NotEmpty(LavaOf(g));
        Assert.NotEmpty(VentsOf(g));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Generation_is_deterministic_with_lava(int seed)
    {
        var a = MapGenerator.Generate(Config(seed, lava: true)).Grid;
        var b = MapGenerator.Generate(Config(seed, lava: true)).Grid;
        Assert.Equal(LavaOf(a), LavaOf(b));
        Assert.Equal(VentsOf(a), VentsOf(b));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Static_lava_is_never_water_adjacent(int seed)
    {
        var g = MapGenerator.Generate(Config(seed, lava: true)).Grid;
        foreach (var p in LavaOf(g))
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n)) Assert.False(g.Get(n).IsWater());
            }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Vents_sit_in_unwalkable_rock_with_a_walkable_neighbor(int seed)
    {
        var g = MapGenerator.Generate(Config(seed, lava: true)).Grid;
        foreach (var v in VentsOf(g))
        {
            Assert.False(g.Get(v).IsWalkable());                       // the vent tile itself is not floor
            Assert.Contains(Card, d => g.InBounds(v + d.ToOffset())    // breachable from the play area
                                       && g.Get(v + d.ToOffset()).IsWalkable());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Spawns_still_reach_center_with_lava(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, lava: true));
        var g = map.Grid;
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
