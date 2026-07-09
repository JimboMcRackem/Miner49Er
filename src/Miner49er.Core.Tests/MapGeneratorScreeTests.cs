using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorScreeTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    [Fact]
    public void No_scree_placed_when_all_counts_are_zero()
    {
        var cfg = new MapConfig { Seed = 1, ScreePatchCount = 0, UnstableRockCount = 0, VolatileRockCount = 0 };
        var map = MapGenerator.Generate(cfg);
        Assert.DoesNotContain(map.Grid.Positions(), p =>
            map.Grid.Get(p) == TileType.ScreeRock ||
            map.Grid.Get(p) == TileType.UnstableRock ||
            map.Grid.Get(p) == TileType.VolatileRock);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void ScreePatchCount_positive_places_ScreeRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, ScreePatchCount = 3 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.ScreeRock);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void UnstableRockCount_positive_places_UnstableRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, UnstableRockCount = 2 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.UnstableRock);
    }

    [Theory]
    [InlineData(42)]
    public void VolatileRockCount_positive_places_VolatileRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, VolatileRockCount = 1 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.VolatileRock);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    public void All_scree_tiles_border_at_least_one_Floor_tile(int seed)
    {
        var cfg = new MapConfig { Seed = seed, ScreePatchCount = 3, UnstableRockCount = 2, VolatileRockCount = 1 };
        var map = MapGenerator.Generate(cfg);
        foreach (var p in map.Grid.Positions())
        {
            if (!map.Grid.Get(p).IsScree()) continue;
            bool bordersFloor = Card.Any(d =>
            {
                var off = d.ToOffset();
                var nb = new GridPos(p.X + off.X, p.Y + off.Y);
                return map.Grid.InBounds(nb) && map.Grid.Get(nb) == TileType.Floor;
            });
            Assert.True(bordersFloor, $"Scree tile at {p} does not border any Floor tile");
        }
    }

    [Fact]
    public void FloorConfig_floor_1_has_no_scree()
    {
        var cfg = MapConfig.FloorConfig(1, seed: 1);
        Assert.Equal(0, cfg.ScreePatchCount);
        Assert.Equal(0, cfg.UnstableRockCount);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_3_has_scree()
    {
        var cfg = MapConfig.FloorConfig(3, seed: 1);
        Assert.True(cfg.ScreePatchCount > 0);
        Assert.Equal(0, cfg.UnstableRockCount);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_8_has_unstable_rock()
    {
        var cfg = MapConfig.FloorConfig(8, seed: 1);
        Assert.True(cfg.UnstableRockCount > 0);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_15_has_volatile_rock()
    {
        var cfg = MapConfig.FloorConfig(15, seed: 1);
        Assert.True(cfg.VolatileRockCount > 0);
    }

    [Fact]
    public void FloorConfig_floor_20_scree_count_never_exceeds_cap()
    {
        var cfg = MapConfig.FloorConfig(20, seed: 1);
        Assert.True(cfg.ScreePatchCount <= 3);
        Assert.True(cfg.UnstableRockCount <= 2);
        Assert.True(cfg.VolatileRockCount <= 1);
    }
}
