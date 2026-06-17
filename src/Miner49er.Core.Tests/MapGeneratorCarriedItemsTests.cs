using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCarriedItemsTests
{
    [Fact]
    public void Generates_the_configured_number_of_plank_and_mold_items()
    {
        var cfg = new MapConfig { Seed = 99, PlayerCount = 4, WaterPlankCount = 3, SlowMoldCount = 3 };
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(3, map.Items.Count(i => i.Kind == ItemKind.WaterPlank));
        Assert.Equal(3, map.Items.Count(i => i.Kind == ItemKind.SlowMold));
    }

    [Fact]
    public void Carried_items_sit_on_walkable_floor_tiles()
    {
        var cfg = new MapConfig { Seed = 7, PlayerCount = 3 };
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(i => i.Kind.IsCarried()))
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(it.Pos));
            Assert.NotEqual(ItemPlacement.Buried, it.Placement);
        }
    }

    [Fact]
    public void Buff_items_are_unaffected_by_the_carried_item_pass()
    {
        // The buried/toolbox buff scatter must only ever contain the three buff kinds.
        var cfg = new MapConfig { Seed = 21, PlayerCount = 4 };
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(i => i.Placement == ItemPlacement.Buried))
            Assert.False(it.Kind.IsCarried());
    }

    [Fact]
    public void Carried_item_placement_is_deterministic_for_a_fixed_seed()
    {
        var a = MapGenerator.Generate(new MapConfig { Seed = 123, PlayerCount = 5 });
        var b = MapGenerator.Generate(new MapConfig { Seed = 123, PlayerCount = 5 });
        var ca = a.Items.Where(i => i.Kind.IsCarried()).Select(i => (i.Pos, i.Kind)).ToList();
        var cb = b.Items.Where(i => i.Kind.IsCarried()).Select(i => (i.Pos, i.Kind)).ToList();
        Assert.Equal(ca, cb);
    }

    [Fact]
    public void Generates_the_configured_number_of_lanterns()
    {
        var cfg = new MapConfig { Seed = 77, PlayerCount = 2, LanternCount = 2 };
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(2, map.Items.Count(i => i.Kind == ItemKind.Lantern));
    }
}
