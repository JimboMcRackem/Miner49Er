using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorTests
{
    private static MapConfig Config(int seed, int players) => new()
    {
        Seed = seed,
        PlayerCount = players,
        BaseWidth = 24,
        BaseHeight = 24,
        SizePerPlayer = 6,
        InitialFloorChance = 0.45f,
        SmoothingSteps = 4,
        MinSpawnDistance = 6,
        GoldVeinCount = 8,
    };

    [Fact]
    public void Dimensions_scale_with_player_count()
    {
        var two = MapGenerator.Generate(Config(1, 2)).Grid;
        var eight = MapGenerator.Generate(Config(1, 8)).Grid;
        Assert.True(eight.Width > two.Width);
        Assert.True(eight.Height > two.Height);
    }

    [Fact]
    public void Border_is_impermeable()
    {
        var grid = MapGenerator.Generate(Config(7, 4)).Grid;
        for (int x = 0; x < grid.Width; x++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 0)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, grid.Height - 1)));
        }
        for (int y = 0; y < grid.Height; y++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(0, y)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(grid.Width - 1, y)));
        }
    }

    [Fact]
    public void Same_seed_produces_identical_maps()
    {
        var a = MapGenerator.Generate(Config(42, 4)).Grid;
        var b = MapGenerator.Generate(Config(42, 4)).Grid;
        Assert.True(a.Positions().All(p => a.Get(p) == b.Get(p)));
    }

    [Fact]
    public void Spawns_count_matches_players_and_are_walkable()
    {
        var map = MapGenerator.Generate(Config(3, 6));
        Assert.Equal(6, map.Spawns.Count);
        Assert.All(map.Spawns, s => Assert.True(map.Grid.IsWalkable(s)));
    }

    [Fact]
    public void Every_spawn_can_reach_the_center()
    {
        var map = MapGenerator.Generate(Config(9, 4));
        var reachable = FloodFillFloor(map.Grid, map.Center);
        Assert.All(map.Spawns, s => Assert.Contains(s, reachable));
    }

    [Fact]
    public void Gold_rock_is_present()
    {
        var grid = MapGenerator.Generate(Config(5, 4)).Grid;
        Assert.Contains(grid.Positions(), p => grid.Get(p) == TileType.GoldRock);
    }

    private static HashSet<GridPos> FloodFillFloor(TileGrid grid, GridPos start)
    {
        var seen = new HashSet<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(start);
        seen.Add(start);
        Direction[] dirs = { Direction.North, Direction.East, Direction.South, Direction.West };
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in dirs)
            {
                var n = p + d.ToOffset();
                if (grid.IsWalkable(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen;
    }
}
