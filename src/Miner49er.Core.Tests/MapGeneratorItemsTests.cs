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
        Assert.Equal(a.Items, b.Items); // same positions, kinds, and placements, same order
    }

    // The buff scatter (auto-apply kinds) is what these tests describe; carried items
    // (water-plank, slow-mold) are a separate visible-Floor pass appended to Items.
    private static System.Collections.Generic.List<Item> BuffItems(GeneratedMap map) =>
        map.Items.Where(it => !it.Kind.IsCarried()).ToList();

    [Fact]
    public void Total_buff_item_count_scales_with_player_count()
    {
        Assert.Equal(9, BuffItems(MapGenerator.Generate(Cfg(players: 1))).Count);  // 9 + 1*0
        Assert.Equal(12, BuffItems(MapGenerator.Generate(Cfg(players: 4))).Count); // 9 + 1*3
    }

    [Fact]
    public void Exactly_VisibleItemCount_buff_toolboxes_on_floor_never_a_spawn()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var spawns = map.Spawns.ToHashSet();
        var toolboxes = BuffItems(map).Where(it => it.Placement == ItemPlacement.Toolbox).ToList();
        Assert.Equal(2, toolboxes.Count); // VisibleItemCount default
        foreach (var it in toolboxes)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(it.Pos));
            Assert.DoesNotContain(it.Pos, spawns);
        }
    }

    [Fact]
    public void The_rest_of_the_buff_scatter_is_buried_in_ordinary_rock()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var buried = map.Items.Where(it => it.Placement == ItemPlacement.Buried).ToList();
        Assert.Equal(BuffItems(map).Count - 2, buried.Count);
        Assert.NotEmpty(buried);
        foreach (var it in buried)
            Assert.Equal(TileType.Rock, map.Grid.Get(it.Pos)); // plain rock, never GoldRock/Impermeable/Floor
    }

    [Fact]
    public void All_item_positions_are_disjoint()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.Equal(map.Items.Count, map.Items.Select(it => it.Pos).Distinct().Count());
    }

    [Fact]
    public void Buff_kinds_are_assigned_round_robin_in_placement_order()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var kinds = System.Enum.GetValues<ItemKind>().Where(k => !k.IsCarried()).ToArray();
        var buff = BuffItems(map);
        for (int i = 0; i < buff.Count; i++)
            Assert.Equal(kinds[i % kinds.Length], buff[i].Kind);
    }
}
