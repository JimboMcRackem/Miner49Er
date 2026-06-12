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
    public void Dropping_a_mold_spreads_patches_over_a_manhattan_disc()
    {
        var sim = MoldSim(out var placer);
        Assert.True(sim.TryUseItem(1));     // empty hand, on empty tile -> use held -> drop mold
        Assert.Null(placer.Held);

        // On a 7x7 all-floor grid centred at (3,3), the whole radius-r disc is in bounds.
        int r = sim.Config.MoldRadius;
        int expected = 2 * r * (r + 1) + 1; // tiles within Manhattan distance r
        Assert.Equal(expected, sim.Molds.Count);
        Assert.Contains(sim.Molds, mo => mo.Pos == new GridPos(3, 3));         // includes the centre
        Assert.All(sim.Molds, mo => Assert.Equal(sim.Config.MoldSeconds, mo.RemainingSeconds, 3));
        Assert.Single(sim.DrainEvents().OfType<MoldDropped>());                // one event, at the centre
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
        Assert.NotEmpty(sim.DrainEvents().OfType<MoldExpired>()); // one per patch in the spread
    }

    [Fact]
    public void Re_dropping_on_an_existing_patch_refreshes_without_duplicating()
    {
        var sim = MoldSim(out var placer);
        sim.TryUseItem(1);                 // drop #1 (a disc)
        int countAfterFirst = sim.Molds.Count;
        sim.Tick(5.0);                     // patches down to ~15s
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold));
        sim.TryUseItem(1);                 // pick up the new one
        sim.TryUseItem(1);                 // drop #2 on the same centre -> refresh the same disc
        Assert.Equal(countAfterFirst, sim.Molds.Count); // no growth: same tiles refreshed
        Assert.All(sim.Molds, mo => Assert.Equal(sim.Config.MoldSeconds, mo.RemainingSeconds, 3));
    }
}
