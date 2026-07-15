using System.Linq;
using Miner49er.Core;
using Xunit;

public class TreasureHeistTests
{
    internal static SimConfig Cfg() => new SimConfig
    {
        Mode = GameMode.TreasureHeist,
        TreasureHeistMode = true,
        BaseMoveSeconds = 0.05,
        TreasureSneakSeconds = 8.0,
        TreasureSneakRadius = 6,
        TreasureSneakCooldown = 10.0,
        SuddenDeathHoldSeconds = 3.0,
        RespawnSeconds = 5.0,
        StartingStones = 9,
    };
    internal static TileGrid Grid(int w = 15, int h = 15) => new TileGrid(w, h, TileType.Floor);

    [Fact]
    public void New_miner_starts_with_configured_stones()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void Stepping_onto_unearthed_treasure_picks_it_up_and_fires_found()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5)); // treasure loose under miner 1
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        Assert.True(sim.TreasureFoundYet);
        Assert.Contains(sim.DrainEvents(), e => e is TreasureFound { MinerId: 1 });
    }

    [Fact]
    public void Buried_treasure_position_is_known_during_find_phase()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(8, 9), ItemKind.IdolUrn, ItemPlacement.Buried));
        Assert.False(sim.TreasureUnearthed);
        Assert.Equal(new GridPos(8, 9), sim.TreasurePos);
    }

    [Fact]
    public void Stun_drops_the_treasure_for_others_to_grab()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        sim.GetMiner(1).StunRemaining = 0.8;        // internal setter (test assembly sees internals)
        sim.Tick(0.1);
        Assert.Equal(-1, sim.TreasureHolderId);     // dropped
        Assert.Equal(new GridPos(5, 5), sim.TreasurePos);
    }

    [Fact]
    public void Stun_drops_held_item_onto_a_free_tile()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.GetMiner(1).Held = ItemKind.Lantern;
        sim.GetMiner(1).StunRemaining = 0.8;
        sim.Tick(0.1);
        Assert.Null(sim.GetMiner(1).Held);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose);
    }

    [Fact]
    public void Carrying_the_treasure_slows_the_holder()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        double baseline = sim.EffectiveMoveSeconds(1);
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        Assert.True(sim.EffectiveMoveSeconds(1) > baseline);
    }

    [Fact]
    public void Lone_carrier_triggers_sneaking_toast_after_threshold()
    {
        var cfg = Cfg(); cfg.TreasureSneakSeconds = 2.0; cfg.TreasureSneakRadius = 3;
        var sim = new Simulation(Grid(20, 20), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddMiner(2, new GridPos(18, 18)); // far away rival
        sim.ForceTreasureLooseForTest(new GridPos(2, 2));
        sim.Tick(0.1); // miner 1 grabs it
        sim.DrainEvents();
        for (int i = 0; i < 30; i++) sim.Tick(0.1); // 3s of lone carrying
        Assert.Contains(sim.DrainEvents(), e => e is TreasureSneaking { MinerId: 1 });
    }

    [Fact]
    public void Thrown_stone_lands_as_a_pickup_in_heist()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.SetFacingForTest(1, Direction.East);
        sim.TryThrowStone(1);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.Stone && it.Placement == ItemPlacement.Loose);
    }

    [Fact]
    public void Dead_miner_respawns_after_delay_when_respawns_enabled()
    {
        var cfg = Cfg(); cfg.TreasureRespawnEnabled = true; cfg.RespawnSeconds = 1.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        Assert.False(sim.GetMiner(1).Alive);
        for (int i = 0; i < 12; i++) sim.Tick(0.1); // > 1s
        Assert.True(sim.GetMiner(1).Alive);
        Assert.Equal(new GridPos(3, 3), sim.GetMiner(1).Pos); // back at spawn
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void Respawn_relocates_off_a_flooded_spawn_to_dry_land()
    {
        var cfg = Cfg(); cfg.TreasureRespawnEnabled = true; cfg.RespawnSeconds = 1.0;
        var grid = Grid();
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        grid.Set(new GridPos(3, 3), TileType.DeepWater); // spawn point submerged while dead
        for (int i = 0; i < 12; i++) sim.Tick(0.1);      // respawn fires
        var m = sim.GetMiner(1);
        Assert.True(m.Alive);
        Assert.NotEqual(new GridPos(3, 3), m.Pos);       // moved off the drowning tile
        Assert.False(grid.Get(m.Pos).IsWater());         // relocated to dry land
        Assert.True(grid.Get(m.Pos).IsWalkable());
    }

    [Fact]
    public void ReviveMiner_relocates_off_deep_water_to_dry_land()
    {
        var grid = Grid();
        var sim = new Simulation(grid, Cfg());
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        grid.Set(new GridPos(3, 3), TileType.DeepWater);
        sim.ReviveMiner(1, new GridPos(3, 3));
        var m = sim.GetMiner(1);
        Assert.True(m.Alive);
        Assert.NotEqual(new GridPos(3, 3), m.Pos);
        Assert.False(grid.Get(m.Pos).IsWater());
    }

    [Fact]
    public void ReviveMiner_keeps_a_dry_spawn_in_place()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        sim.ReviveMiner(1, new GridPos(4, 4)); // already dry floor
        Assert.Equal(new GridPos(4, 4), sim.GetMiner(1).Pos);
    }

    [Fact]
    public void Death_match_keeps_dead_miner_out()
    {
        var cfg = Cfg(); cfg.TreasureRespawnEnabled = false;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        for (int i = 0; i < 100; i++) sim.Tick(0.1);
        Assert.False(sim.GetMiner(1).Alive);
    }

    [Fact]
    public void Heist_map_has_no_scattered_gold_or_buff_items()
    {
        var cfg = MapConfig.For(GameMode.TreasureHeist, seed: 42, playerCount: 4,
            pits: false, caveIns: false, lava: false, mapScale: 1, explosive: ExplosiveMode.Dynamite);
        Assert.Equal(0, cfg.GoldVeinCount);
        Assert.Equal(0, cfg.BaseItemCount);
        Assert.Equal(0, cfg.VisibleItemCount);
    }

    [Fact]
    public void Buried_non_idol_does_not_hijack_the_treasure()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddItem(new Item(new GridPos(8, 9), ItemKind.IdolUrn, ItemPlacement.Buried)); // the treasure
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Buried)); // must not hijack
        Assert.Equal(new GridPos(8, 9), sim.TreasurePos);
    }

    [Fact]
    public void Space_cannot_grab_the_loose_treasure_urn_in_heist()
    {
        var sim = new Simulation(Grid(), Cfg());
        var pos = new GridPos(5, 5);
        sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.IdolUrn, ItemPlacement.Loose)); // urn on the miner's tile
        bool used = sim.TryUseItem(1);
        Assert.False(used);                       // Space did nothing
        Assert.Null(sim.GetMiner(1).Held);        // urn was NOT taken into the held slot
    }

    [Fact]
    public void Sudden_death_progress_accrues_for_a_lone_holder_after_expiry()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);                       // miner 1 grabs it
        Assert.Equal(0.0, sim.SuddenDeathProgress);
        sim.SetTimeExpiredForTest();
        for (int i = 0; i < 10; i++) sim.Tick(0.1); // ~1s of uncontested overtime holding
        Assert.True(sim.SuddenDeathProgress > 0.0);
        Assert.True(sim.SuddenDeathProgress <= 1.0);
    }
}
