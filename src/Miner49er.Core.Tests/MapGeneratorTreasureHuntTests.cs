using Miner49er.Core;
using System.Linq;
using Xunit;

public class MapGeneratorTreasureHuntTests
{
    [Fact]
    public void Generate_places_exactly_playerCount_times_two_idols()
    {
        int playerCount = 3;
        var cfg = MapConfig.For(GameMode.TreasureHunt, 42, playerCount);
        var map = MapGenerator.Generate(cfg);
        var idols = map.Items.Where(it => it.Kind.IsIdol()).ToList();
        Assert.Equal(playerCount * 2, idols.Count);
    }

    [Fact]
    public void All_placed_idols_are_buried()
    {
        var cfg = MapConfig.For(GameMode.TreasureHunt, 77, 2);
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(it => it.Kind.IsIdol()))
            Assert.Equal(ItemPlacement.Buried, it.Placement);
    }

    [Fact]
    public void No_gold_veins_in_treasure_hunt()
    {
        var cfg = MapConfig.For(GameMode.TreasureHunt, 1, 2);
        Assert.Equal(0, cfg.GoldVeinCount);
    }

    [Fact]
    public void Placed_idol_kinds_match_assignment_for_seed()
    {
        int seed = 55, players = 2;
        var cfg = MapConfig.For(GameMode.TreasureHunt, seed, players);
        var map = MapGenerator.Generate(cfg);
        var assigned = TreasureAssignment.AllAssigned(seed, players);
        var placed = map.Items.Where(it => it.Kind.IsIdol()).Select(it => it.Kind).ToHashSet();
        foreach (var kind in assigned)
            Assert.Contains(kind, placed);
    }

    [Fact]
    public void Idols_sit_in_rock_tiles()
    {
        var cfg = MapConfig.For(GameMode.TreasureHunt, 1, 2);
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(it => it.Kind.IsIdol()))
            Assert.Equal(TileType.Rock, map.Grid.Get(it.Pos));
    }
}
