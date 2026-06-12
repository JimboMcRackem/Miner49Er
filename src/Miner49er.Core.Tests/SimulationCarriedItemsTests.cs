using Miner49er.Core;
using Xunit;

public class SimulationCarriedItemsTests
{
    private static Simulation Sim(out Miner m)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void A_new_miner_starts_with_an_empty_hand()
    {
        Sim(out var m);
        Assert.Null(m.Held);
    }

    [Fact]
    public void Walking_over_a_carried_item_does_not_auto_collect_it()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));
        sim.TryMove(1, Direction.East); // step onto (2,2)
        sim.Tick(0.0);                  // walk-over pickup pass runs in Tick
        Assert.Single(sim.Items);       // still on the ground
        Assert.Null(m.Held);            // not taken
    }

    [Fact]
    public void WaterPlank_and_SlowMold_report_as_carried()
    {
        Assert.True(ItemKind.WaterPlank.IsCarried());
        Assert.True(ItemKind.SlowMold.IsCarried());
        Assert.False(ItemKind.SpeedPotion.IsCarried());
        Assert.False(ItemKind.LongerVision.IsCarried());
        Assert.False(ItemKind.BiggerBlast.IsCarried());
    }
}
