using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCrystalTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    [Fact]
    public void No_crystals_placed_when_CrystalPatchCount_is_zero()
    {
        var cfg = new MapConfig { Seed = 1, CrystalPatchCount = 0 };
        var map = MapGenerator.Generate(cfg);
        Assert.DoesNotContain(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.CrystalRock);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    [InlineData(99)]
    public void Crystals_are_placed_when_CrystalPatchCount_is_positive(int seed)
    {
        var cfg = new MapConfig { Seed = seed, CrystalPatchCount = 4 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.CrystalRock);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    public void All_CrystalRock_tiles_border_at_least_one_Floor_tile(int seed)
    {
        var cfg = new MapConfig { Seed = seed, CrystalPatchCount = 4 };
        var map = MapGenerator.Generate(cfg);
        foreach (var p in map.Grid.Positions())
        {
            if (map.Grid.Get(p) != TileType.CrystalRock) continue;
            bool bordersFloor = Card.Any(d =>
            {
                var off = d.ToOffset();
                var nb = new GridPos(p.X + off.X, p.Y + off.Y);
                return map.Grid.InBounds(nb) && map.Grid.Get(nb) == TileType.Floor;
            });
            Assert.True(bordersFloor, $"CrystalRock at {p} does not border any Floor tile");
        }
    }

    [Fact]
    public void FloorConfig_floor_1_has_no_crystals()
    {
        var cfg = MapConfig.FloorConfig(1, seed: 1);
        Assert.Equal(0, cfg.CrystalPatchCount);
    }

    [Fact]
    public void FloorConfig_floor_3_has_crystals()
    {
        var cfg = MapConfig.FloorConfig(3, seed: 1);
        Assert.True(cfg.CrystalPatchCount > 0);
    }
}
