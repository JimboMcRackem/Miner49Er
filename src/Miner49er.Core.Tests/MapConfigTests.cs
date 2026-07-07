using Miner49er.Core;
using Xunit;

public class MapConfigTests
{
    [Fact]
    public void Last_man_standing_uses_base_map_settings()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 7, playerCount: 3);
        Assert.Equal(7, cfg.Seed);
        Assert.Equal(3, cfg.PlayerCount);
        Assert.Equal(32, cfg.BaseWidth);
        Assert.Equal(32, cfg.BaseHeight);
        Assert.Equal(0.45f, cfg.InitialFloorChance);
    }

    [Fact]
    public void Gold_rush_also_uses_base_map_settings()
    {
        var cfg = MapConfig.For(GameMode.GoldRush, seed: 1, playerCount: 1);
        Assert.Equal(32, cfg.BaseWidth);
        Assert.Equal(32, cfg.BaseHeight);
        Assert.Equal(0.45f, cfg.InitialFloorChance);
    }

    [Fact]
    public void Reach_center_uses_a_larger_denser_map()
    {
        var cfg = MapConfig.For(GameMode.ReachCenter, seed: 1, playerCount: 1);
        Assert.Equal(64, cfg.BaseWidth);
        Assert.Equal(64, cfg.BaseHeight);
        Assert.Equal(0.42f, cfg.InitialFloorChance);
    }

    [Fact]
    public void For_dynamite_mode_sets_zero_detonators()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 3,
            explosive: ExplosiveMode.Dynamite);
        Assert.Equal(0, cfg.DetonatorCount);
    }

    [Fact]
    public void For_detonator_specials_sets_one_detonator_per_player()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 3,
            explosive: ExplosiveMode.DetonatorSpecials);
        Assert.Equal(3, cfg.DetonatorCount);
    }

    [Fact]
    public void For_detonators_only_sets_one_detonator_per_player()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 2,
            explosive: ExplosiveMode.DetonatorsOnly);
        Assert.Equal(2, cfg.DetonatorCount);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(48)]
    public void FloorConfig_every_4th_floor_has_expedition_treasure(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 1, playerCount: 1);
        Assert.NotNull(cfg.ExpeditionTreasureKind);
        Assert.True(cfg.ExpeditionTreasureKind!.Value.IsIdol());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(49)]
    public void FloorConfig_non_4th_floor_has_no_expedition_treasure(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 1, playerCount: 1);
        Assert.Null(cfg.ExpeditionTreasureKind);
    }

    [Fact]
    public void FloorConfig_floor_4_treasure_is_deterministic()
    {
        var cfg1 = MapConfig.FloorConfig(4, seed: 42, playerCount: 1);
        var cfg2 = MapConfig.FloorConfig(4, seed: 42, playerCount: 1);
        Assert.Equal(cfg1.ExpeditionTreasureKind, cfg2.ExpeditionTreasureKind);
        Assert.Equal(cfg1.ExpeditionTreasureInChest, cfg2.ExpeditionTreasureInChest);
    }
}
