using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorDecoysTests
{
    private static MapConfig Cfg(int seed = 7, int players = 1) =>
        new() { Seed = seed, PlayerCount = players };

    [Fact]
    public void Decoy_count_matches_config()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.Equal(4, map.Decoys.Count); // DecoyCount default
    }

    [Fact]
    public void Decoys_sit_on_ordinary_rock()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.NotEmpty(map.Decoys);
        foreach (var d in map.Decoys)
            Assert.Equal(TileType.Rock, map.Grid.Get(d)); // never GoldRock/Impermeable/Floor
    }

    [Fact]
    public void Decoys_are_disjoint_from_item_positions()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var itemPositions = map.Items.Select(it => it.Pos).ToHashSet();
        foreach (var d in map.Decoys)
            Assert.DoesNotContain(d, itemPositions);
    }

    [Fact]
    public void Decoy_placement_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(Cfg());
        var b = MapGenerator.Generate(Cfg());
        Assert.Equal(a.Decoys, b.Decoys);
    }
}
