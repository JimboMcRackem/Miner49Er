using Miner49er.Core;
using Xunit;

public class MapConfigFloorTests
{
    [Theory]
    [InlineData(1)]  [InlineData(3)]  [InlineData(5)]
    public void Floors_1_to_5_are_small_no_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(32, cfg.BaseWidth);
        Assert.Equal(32, cfg.BaseHeight);
        Assert.False(cfg.Pits);
        Assert.False(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(6)]  [InlineData(8)]  [InlineData(10)]
    public void Floors_6_to_10_are_medium_with_pits(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(48, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.False(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(11)] [InlineData(13)] [InlineData(15)]
    public void Floors_11_to_15_are_large_with_pits_and_caveins(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(64, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(16)] [InlineData(18)] [InlineData(20)]
    public void Floors_16_to_20_are_huge_all_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(80, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.True(cfg.Lava);
    }

    [Theory]
    [InlineData(1, 42)] [InlineData(1, 99)]
    public void Different_seeds_give_same_difficulty_structure(int floor, int seed)
    {
        var cfg = MapConfig.FloorConfig(floor, seed);
        Assert.Equal(32, cfg.BaseWidth);
        Assert.Equal(seed, cfg.Seed);
    }

    [Theory]
    [InlineData(1, 2)] [InlineData(1, 4)] [InlineData(6, 3)]
    public void FloorConfig_with_multiple_players_sets_player_count(int floor, int playerCount)
    {
        var cfg = MapConfig.FloorConfig(floor, 42, playerCount);
        Assert.Equal(playerCount, cfg.PlayerCount);
    }

    [Theory]
    [InlineData(21)] [InlineData(25)] [InlineData(30)]
    public void Floors_21_to_30_are_scale5_all_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(96, cfg.BaseWidth);
        Assert.Equal(96, cfg.BaseHeight);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.True(cfg.Lava);
    }

    [Theory]
    [InlineData(31)] [InlineData(35)] [InlineData(40)]
    public void Floors_31_to_40_are_scale6_all_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(112, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.True(cfg.Lava);
    }

    [Theory]
    [InlineData(41)] [InlineData(45)] [InlineData(50)]
    public void Floors_41_to_50_are_scale7_all_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(128, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.True(cfg.Lava);
    }

    [Fact]
    public void MonsterRoster_has_extra_bonuses_at_floors_20_and_28()
    {
        int count14 = MonsterRoster.CountFor(32, 32, 14);
        int count20 = MonsterRoster.CountFor(32, 32, 20);
        int count28 = MonsterRoster.CountFor(32, 32, 28);
        Assert.True(count20 > count14);
        Assert.True(count28 > count20);
    }
}
