using Miner49er.Core;
using Xunit;

// Grudge Match mode: open arena map, infinite respawns, kills persist across deaths.
public class GrudgeMatchTests
{
    [Fact]
    public void Grudge_map_strips_loot_and_opens_up()
    {
        var cfg = MapConfig.For(GameMode.GrudgeMatch, seed: 3, playerCount: 4);
        Assert.Equal(0, cfg.GoldVeinCount);
        Assert.Equal(0, cfg.BaseItemCount);
        Assert.Equal(0, cfg.ChestCount);
        Assert.Equal(0.60f, cfg.InitialFloorChance); // more open than the 0.45 base
        Assert.True(cfg.StonePileCount >= 8);         // generous throwables
    }

    [Fact]
    public void Respawn_is_enabled_in_Grudge_even_with_treasure_respawn_off()
    {
        var sim = new Simulation(new TileGrid(8, 8, TileType.Floor),
            new SimConfig { Mode = GameMode.GrudgeMatch, TreasureRespawnEnabled = false });
        Assert.True(sim.RespawnEnabled);
    }

    [Fact]
    public void A_killed_Grudge_miner_revives_after_the_respawn_delay_keeping_its_kills()
    {
        var sim = new Simulation(new TileGrid(8, 8, TileType.Floor),
            new SimConfig { Mode = GameMode.GrudgeMatch, RespawnSeconds = 5.0 });
        var m = sim.AddMiner(1, new GridPos(3, 3));
        m.Kills = 4;

        sim.KillMiner(1);
        Assert.False(m.Alive);

        sim.Tick(5.1); // past the respawn delay
        Assert.True(m.Alive);
        Assert.Equal(4, m.Kills); // score carries across respawns
    }
}
