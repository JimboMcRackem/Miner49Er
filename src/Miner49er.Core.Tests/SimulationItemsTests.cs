using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationItemsTests
{
    private static Simulation Sim(out Miner m)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void Walking_onto_an_item_collects_it_and_applies_the_buff()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));

        sim.TryMove(1, Direction.East); // step onto (2,2)
        sim.Tick(0.0);                  // pickup pass runs in Tick

        Assert.Empty(sim.Items);
        var e = Assert.Single(m.Effects);
        Assert.Equal(EffectKind.SpeedPotion, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
    }

    [Fact]
    public void LongerVision_item_raises_effective_vision_radius()
    {
        var sim = Sim(out _);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(8, sim.EffectiveVisionRadius(1)); // 5 + VisionBonus(3)
    }

    [Fact]
    public void A_collected_item_is_gone_for_everyone_else()
    {
        var sim = Sim(out _);
        sim.AddMiner(2, new GridPos(3, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));

        sim.TryMove(1, Direction.East); // miner 1 onto (2,2)
        sim.Tick(0.0);                  // collected by 1
        sim.TryMove(2, Direction.West); // miner 2 onto (2,2)
        sim.Tick(0.0);

        Assert.Empty(sim.Items);
        Assert.Empty(sim.GetMiner(2).Effects); // nothing left to pick up
    }

    [Fact]
    public void A_dead_miner_does_not_collect_an_item_under_it()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(1, 2), ItemKind.SpeedPotion)); // on the miner's tile
        sim.KillMiner(1);
        sim.Tick(0.0);
        Assert.Single(sim.Items);
    }

    [Fact]
    public void Pickup_emits_an_ItemPickedUp_event()
    {
        var sim = Sim(out _);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        var ev = Assert.Single(sim.DrainEvents().OfType<ItemPickedUp>());
        Assert.Equal(1, ev.MinerId);
        Assert.Equal(new GridPos(2, 2), ev.Pos);
        Assert.Equal(ItemKind.BiggerBlast, ev.Kind);
    }
}
