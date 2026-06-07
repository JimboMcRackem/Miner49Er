using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorWaterTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed) => new() { Seed = seed, PlayerCount = 4 };

    private static bool IsWater(TileType t) => t is TileType.ShallowWater or TileType.DeepWater;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Water_is_generated(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed)).Grid;
        Assert.Contains(grid.Positions(), p => grid.Get(p) == TileType.ShallowWater);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Deep_water_is_always_ringed_by_water(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed)).Grid;
        foreach (var p in grid.Positions())
        {
            if (grid.Get(p) != TileType.DeepWater) continue;
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                Assert.True(grid.InBounds(n) && IsWater(grid.Get(n)),
                    $"deep tile {p} has non-water neighbour {n}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Spawns_are_floor_and_not_water_adjacent(int seed)
    {
        var map = MapGenerator.Generate(Config(seed));
        foreach (var s in map.Spawns)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(s));
            foreach (var d in Card)
            {
                var n = s + d.ToOffset();
                if (map.Grid.InBounds(n))
                    Assert.False(IsWater(map.Grid.Get(n)), $"spawn {s} touches water at {n}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void All_spawns_and_gold_are_reachable_without_deep_water(int seed)
    {
        var map = MapGenerator.Generate(Config(seed));
        // Flood over Floor + ShallowWater only (deep water is a wall).
        var reachable = new HashSet<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(map.Spawns[0]); reachable.Add(map.Spawns[0]);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (map.Grid.InBounds(n) && map.Grid.Get(n).IsWalkable() && reachable.Add(n))
                    stack.Push(n);
            }
        }
        Assert.All(map.Spawns, s => Assert.Contains(s, reachable));
        foreach (var p in map.Grid.Positions())
            if (map.Grid.Get(p) == TileType.GoldRock)
                Assert.Contains(Card.Select(d => p + d.ToOffset()), n => reachable.Contains(n));
    }

    [Fact]
    public void Same_seed_produces_identical_water()
    {
        var a = MapGenerator.Generate(Config(99)).Grid;
        var b = MapGenerator.Generate(Config(99)).Grid;
        Assert.True(a.Positions().All(p => a.Get(p) == b.Get(p)));
    }
}
