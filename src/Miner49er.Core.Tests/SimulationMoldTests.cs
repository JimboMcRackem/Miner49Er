using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMoldTests
{
    private static Simulation MoldSim(out Miner placer)
    {
        var sim = new Simulation(new TileGrid(7, 7, TileType.Floor), new SimConfig());
        placer = sim.AddMiner(1, new GridPos(3, 3));
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold)); // under the placer
        sim.TryUseItem(1);                                            // pick it up
        return sim;
    }

    [Fact]
    public void Dropping_a_mold_places_a_patch_and_empties_the_hand()
    {
        var sim = MoldSim(out var placer);
        Assert.True(sim.TryUseItem(1));     // empty hand, on empty tile -> use held -> drop mold
        Assert.Null(placer.Held);
        var patch = Assert.Single(sim.Molds);
        Assert.Equal(new GridPos(3, 3), patch.Pos);
        Assert.Equal(sim.Config.MoldSeconds, patch.RemainingSeconds, 3);
        Assert.Single(sim.DrainEvents().OfType<MoldDropped>());
    }

    [Fact]
    public void The_placer_standing_on_their_own_mold_is_not_slowed()
    {
        var sim = MoldSim(out var placer);
        sim.TryUseItem(1);          // drop under self
        sim.Tick(0.1);
        Assert.Empty(placer.Effects); // dropping is not "stepping on"
    }

    [Fact]
    public void A_miner_stepping_onto_a_mold_is_slowed()
    {
        var sim = MoldSim(out _);
        sim.TryUseItem(1);                       // mold at (3,3)
        var other = sim.AddMiner(2, new GridPos(2, 3));
        sim.TryMove(2, Direction.East);          // step onto (3,3)
        var e = Assert.Single(other.Effects);
        Assert.Equal(EffectKind.SlowMold, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
        Assert.Equal(sim.Config.MoldSlowFactor, e.Magnitude, 3);
    }

    [Fact]
    public void A_mold_patch_decays_and_expires()
    {
        var sim = MoldSim(out _);
        sim.TryUseItem(1);
        sim.Tick(sim.Config.MoldSeconds + 0.01);
        Assert.Empty(sim.Molds);
        Assert.Single(sim.DrainEvents().OfType<MoldExpired>());
    }

    [Fact]
    public void Re_dropping_on_an_existing_patch_refreshes_without_duplicating()
    {
        var sim = MoldSim(out var placer);
        sim.TryUseItem(1);                 // drop #1
        sim.Tick(5.0);                     // patch down to ~15s
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold));
        sim.TryUseItem(1);                 // pick up the new one
        sim.TryUseItem(1);                 // drop #2 on the same tile -> refresh
        var patch = Assert.Single(sim.Molds);
        Assert.Equal(sim.Config.MoldSeconds, patch.RemainingSeconds, 3);
    }
}
