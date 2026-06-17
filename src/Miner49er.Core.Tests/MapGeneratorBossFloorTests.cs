using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorBossFloorTests
{
    private static GeneratedMap Make(int seed = 1) => MapGenerator.GenerateBossFloor(seed);

    [Fact]
    public void Boss_floor_is_40_by_40()
    {
        var map = Make();
        Assert.Equal(40, map.Grid.Width);
        Assert.Equal(40, map.Grid.Height);
    }

    [Fact]
    public void Border_is_impermeable_rock()
    {
        var map  = Make();
        var grid = map.Grid;
        for (int x = 0; x < 40; x++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 0)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 39)));
        }
        for (int y = 0; y < 40; y++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(0, y)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(39, y)));
        }
    }

    [Fact]
    public void Central_island_is_floor()
    {
        var map    = Make();
        var center = map.Center;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Assert.Equal(TileType.Floor,
                    map.Grid.Get(new GridPos(center.X + dx, center.Y + dy)));
    }

    [Fact]
    public void Chest_is_one_south_of_center()
    {
        var map    = Make();
        var center = map.Center;
        var chest  = map.Items.FirstOrDefault(i => i.Kind == ItemKind.Chest);
        Assert.Equal(new GridPos(center.X, center.Y + 1), chest.Pos);
    }

    [Fact]
    public void Spawn_is_walkable()
    {
        var map = Make();
        Assert.Single(map.Spawns);
        var spawnTile = map.Grid.Get(map.Spawns[0]);
        Assert.True(spawnTile == TileType.Floor || spawnTile == TileType.ShallowWater);
    }

    [Fact]
    public void Interior_corner_tiles_are_deep_water()
    {
        var map    = Make();
        var center = map.Center;
        // Far diagonal from center — should be deep water
        var farCorner = new GridPos(center.X - 8, center.Y - 8);
        Assert.Equal(TileType.DeepWater, map.Grid.Get(farCorner));
    }

    [Fact]
    public void Same_seed_produces_identical_boss_floors()
    {
        var a = Make(42);
        var b = Make(42);
        Assert.True(a.Grid.Positions().All(p => a.Grid.Get(p) == b.Grid.Get(p)));
    }
}
