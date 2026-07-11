using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorStoneTests
{
    private static bool NearScree(TileGrid g, GridPos p)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new GridPos(p.X + dx, p.Y + dy);
                if (g.InBounds(n) && g.Get(n).IsScree()) return true;
            }
        return false;
    }

    [Fact]
    public void Stones_are_scattered_on_the_floor()
    {
        var cfg = new MapConfig { Seed = 3, StonePileCount = 10 };
        var map = MapGenerator.Generate(cfg);
        var stones = map.Items.Where(it => it.Kind == ItemKind.Stone).ToList();
        Assert.NotEmpty(stones);
        Assert.All(stones, s => Assert.Equal(TileType.Floor, map.Grid.Get(s.Pos)));
        Assert.All(stones, s => Assert.Equal(ItemPlacement.Loose, s.Placement));
    }

    [Fact]
    public void No_stones_when_count_is_zero()
    {
        var cfg = new MapConfig { Seed = 3, StonePileCount = 0 };
        var map = MapGenerator.Generate(cfg);
        Assert.DoesNotContain(map.Items, it => it.Kind == ItemKind.Stone);
    }

    [Fact]
    public void Stone_placement_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(new MapConfig { Seed = 99, StonePileCount = 12 });
        var b = MapGenerator.Generate(new MapConfig { Seed = 99, StonePileCount = 12 });
        var sa = a.Items.Where(it => it.Kind == ItemKind.Stone).Select(it => it.Pos).ToList();
        var sb = b.Items.Where(it => it.Kind == ItemKind.Stone).Select(it => it.Pos).ToList();
        Assert.Equal(sa, sb);
    }

    [Fact]
    public void Stones_are_over_represented_near_scree_walls()
    {
        // Plenty of scree so there are many dangerous-wall-adjacent floor tiles.
        var cfg = new MapConfig { Seed = 5, ScreePatchCount = 6, StonePileCount = 12 };
        var map = MapGenerator.Generate(cfg);

        var stones = map.Items.Where(it => it.Kind == ItemKind.Stone).ToList();
        Assert.NotEmpty(stones);

        var floorCands = map.Grid.Positions().Where(p => map.Grid.Get(p) == TileType.Floor).ToList();
        double baseRate  = floorCands.Count(p => NearScree(map.Grid, p)) / (double)floorCands.Count;
        double stoneRate = stones.Count(s => NearScree(map.Grid, s.Pos)) / (double)stones.Count;

        // The scatter should favour scree-adjacent tiles well above their share of the floor.
        Assert.True(stoneRate > baseRate,
            $"stones near scree {stoneRate:P0} should exceed base rate {baseRate:P0}");
    }
}
