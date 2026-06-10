using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorItemsTests
{
    private static MapConfig Cfg(int seed = 7, int players = 1) =>
        new() { Seed = seed, PlayerCount = players };

    [Fact]
    public void Placement_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(Cfg());
        var b = MapGenerator.Generate(Cfg());
        Assert.Equal(a.Items, b.Items); // same positions and kinds, same order
    }

    [Fact]
    public void Items_land_on_floor_and_never_on_a_spawn()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var spawns = map.Spawns.ToHashSet();
        Assert.NotEmpty(map.Items);
        foreach (var item in map.Items)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(item.Pos));
            Assert.DoesNotContain(item.Pos, spawns);
        }
    }

    [Fact]
    public void Item_count_scales_with_player_count()
    {
        Assert.Equal(9, MapGenerator.Generate(Cfg(players: 1)).Items.Count);  // 9 + 1*0
        Assert.Equal(12, MapGenerator.Generate(Cfg(players: 4)).Items.Count); // 9 + 1*3
    }

    [Fact]
    public void Kinds_are_assigned_round_robin_in_placement_order()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var kinds = System.Enum.GetValues<ItemKind>();
        for (int i = 0; i < map.Items.Count; i++)
            Assert.Equal(kinds[i % kinds.Length], map.Items[i].Kind);
    }
}
