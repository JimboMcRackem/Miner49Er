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
}
